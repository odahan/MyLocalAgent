using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Evaluation;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly JsonSerializerOptions LineJson = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main(string[] args)
    {
        using var lifetime = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; lifetime.Cancel(); };
        try { return await ExecuteAsync(args, lifetime.Token); }
        catch (OperationCanceledException) { Console.Error.WriteLine("Cancelled; completed observations remain checkpointed."); return 130; }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        { Console.Error.WriteLine(exception is LocalDecisionException provider ? provider.Message : exception.Message); return 1; }
    }

    private static async Task<int> ExecuteAsync(string[] args, CancellationToken token)
    {
        if (args.Length == 0 || args[0] is not ("plan" or "run" or "analyze" or "import-vitamin" or "extract-vitamin"))
        {
            Console.WriteLine("Commands: plan|run --corpus PATH --output DIR [--approval PATH] [--repetitions 3] [--config PATH] [--general-model TAG] [--backends rule,native,llm] [--split development|held-out|all]; analyze --output DIR");
            return 2;
        }
        var flags = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 == args.Length || !flags.TryAdd(args[index], args[index + 1]))
                throw new ArgumentException("Expected unique --name value arguments.");
        }
        var allowed = new[] { "--output", "--corpus", "--approval", "--config", "--repetitions", "--split", "--source", "--manifest", "--limit", "--general-model", "--backends" };
        if (flags.Keys.Any(name => !allowed.Contains(name, StringComparer.Ordinal))) throw new ArgumentException("Unknown command option.");
        var directory = Path.GetFullPath(Required(flags, "--output"));
        if (args[0] == "extract-vitamin")
        {
            VitaminCImporter.ExtractDevelopment(Required(flags, "--source"), directory);
            return 0;
        }
        if (args[0] == "import-vitamin")
        {
            VitaminCImporter.Import(Required(flags, "--source"), Required(flags, "--manifest"), directory,
                int.Parse(flags.GetValueOrDefault("--limit", "40"), System.Globalization.CultureInfo.InvariantCulture));
            return 0;
        }
        if (args[0] == "analyze")
        {
            var rows = File.ReadLines(Path.Combine(directory, "rows.jsonl")).Where(l => l.Length > 0)
                .Select(l => JsonSerializer.Deserialize<EvaluationObservation>(l, LineJson)!).ToArray();
            using var savedManifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "manifest.json")));
            EvaluationAnalysis.Write(directory, rows, savedManifest.RootElement.GetProperty("decision").GetProperty("minConfidencePercent").GetDouble() / 100);
            return 0;
        }
        var corpusPath = Path.GetFullPath(Required(flags, "--corpus"));
        var allCases = EvaluationCorpus.Load(corpusPath);
        var split = flags.GetValueOrDefault("--split", "all");
        if (split is not ("all" or "development" or "held-out" or "public")) throw new ArgumentException("Unknown partition.");
        var cases = allCases.Where(c => split == "all" || c.Split == split).ToArray();
        if (cases.Length == 0) throw new ArgumentException("The selected partition is empty.");
        if (!int.TryParse(flags.GetValueOrDefault("--repetitions", "3"), out var repetitions) || repetitions is < 1 or > 10)
            throw new ArgumentException("Repetitions must be between 1 and 10.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new IOException("Use an empty output directory. Existing evidence is never overwritten.");
        Directory.CreateDirectory(directory);
        var fixtureHash = Hash(File.ReadAllBytes(corpusPath));
        var configPath = Path.GetFullPath(flags.GetValueOrDefault("--config", "appsettings.json"));
        var configuration = new ConfigurationBuilder().AddJsonFile(configPath, optional: false)
            .AddJsonFile(LocalAppPaths.UserSettingsPath, optional: true).Build();
        if (configuration.GetSection("AI").GetValue<string>("Provider") != "Ollama")
            throw new InvalidOperationException("This benchmark requires the configured local Ollama provider.");
        var settings = DecisionOptions.Resolve(configuration) with { Enabled = true };
        var local = configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>() ?? new();
        if (flags.TryGetValue("--general-model", out var generalModel))
        {
            if (string.IsNullOrWhiteSpace(generalModel)) throw new ArgumentException("General model tag must not be empty.");
            local = local with { Model = generalModel };
        }
        if (!DecisionOptions.IsValid(settings) || !LocalDecisionEndpoint.IsValid(settings.DecisionEndpoint ?? local.Endpoint) || !LocalDecisionEndpoint.IsValid(local.Endpoint))
            throw new InvalidOperationException("Bounded decision settings and loopback endpoints are required.");
        var approvalPath = flags.GetValueOrDefault("--approval");
        JsonElement? approval = null;
        EvaluationCriteria? criteria = null;
        if (args[0] == "run")
        {
            if (approvalPath is null) throw new InvalidOperationException("Run requires a frozen human approval artifact.");
            using var approved = JsonDocument.Parse(File.ReadAllText(approvalPath));
            if (!approved.RootElement.GetProperty("executionAuthorized").GetBoolean() ||
                !approved.RootElement.GetProperty("labelsApproved").GetBoolean() ||
                approved.RootElement.GetProperty("fixtureSha256").GetString() != fixtureHash)
                throw new InvalidOperationException("Approval must authorize execution and labels for this exact fixture hash.");
            criteria = JsonSerializer.Deserialize<EvaluationCriteria>(approved.RootElement.GetProperty("criteria"), LineJson)
                ?? throw new InvalidOperationException("Approval criteria are required.");
            criteria.Validate();
            approval = approved.RootElement.Clone();
        }
        var knowledge = configuration.GetSection(KnowledgeOptions.SectionName).Get<KnowledgeOptions>() ?? new();
        var concurrency = configuration.GetSection(LlmExecutionOptions.SectionName).Get<LlmExecutionOptions>() ?? new();
        var configurationHash = Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { settings, local, knowledge.EmbeddingModel, concurrency }, LineJson)));
        using var corpusDocument = JsonDocument.Parse(File.ReadAllText(corpusPath));
        var readiness = corpusDocument.RootElement.TryGetProperty("fixtureVersion", out var fixtureVersion) && fixtureVersion.GetString() == "readiness-pairs-v1";
        var availableBackends = readiness ? new[] { "rule", "native", "llm" } : new[] { "native", "llm" };
        var backends = flags.TryGetValue("--backends", out var selectedBackends)
            ? selectedBackends.Split(',', StringSplitOptions.TrimEntries)
            : availableBackends;
        if (backends.Length == 0 || backends.Distinct(StringComparer.Ordinal).Count() != backends.Length ||
            backends.Any(backend => !availableBackends.Contains(backend, StringComparer.Ordinal)))
            throw new ArgumentException("Select unique available backends: " + string.Join(',', availableBackends));
        var manifestPath = Path.Combine(directory, "manifest.json");
        var runId = Guid.NewGuid().ToString("N");
        var sourceDirectory = Path.Combine(Path.GetDirectoryName(configPath)!, "tools", "DecisionEvaluation");
        var sourceHashes = Directory.Exists(sourceDirectory) ? Directory.GetFiles(sourceDirectory, "*.cs")
            .ToDictionary(path => Path.GetFileName(path)!, path => Hash(File.ReadAllBytes(path))) : new Dictionary<string, string>();
        var manifest = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1, ["runId"] = runId, ["sourceHashes"] = sourceHashes,
            ["assemblyHashes"] = new[] { typeof(EvaluationExecution).Assembly, typeof(DecisionEvaluationService).Assembly, typeof(OllamaLocalDecisionClient).Assembly }
                .ToDictionary(assembly => assembly.GetName().Name!, assembly => Hash(File.ReadAllBytes(assembly.Location))),
            ["startedUtc"] = DateTimeOffset.UtcNow, ["fixtureSha256"] = fixtureHash,
            ["configurationSha256"] = configurationHash, ["decision"] = settings, ["ollama"] = local,
            ["embeddingModel"] = knowledge.EmbeddingModel, ["concurrency"] = concurrency,
            ["generalModelOverride"] = flags.GetValueOrDefault("--general-model"),
            ["generalInstructions"] = EvaluationExecution.GeneralInstructions, ["generalOptions"] = new { temperature = 0, num_ctx = 8192, num_predict = 512, think = false, keep_alive = "10s" },
            ["caseCount"] = cases.Length, ["repetitions"] = repetitions, ["split"] = split, ["backends"] = backends,
            ["approval"] = approval, ["gitRevision"] = Revision(), ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["os"] = RuntimeInformation.OSDescription, ["processors"] = Environment.ProcessorCount,
            ["availableMemoryBytes"] = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
            ["completion"] = args[0] == "plan" ? "plan-only" : "started", ["nativeCondition"] = "product one-shot with cleanup; no warm batch lease",
            ["enablement"] = "Enabled only in this isolated process; application settings are never saved.",
            ["coldObservation"] = "Native: provider residency observation. General: digest resident before call; context replacement may still reload a resident model.",
        };
        File.Copy(corpusPath, Path.Combine(directory, "corpus.json"));
        SaveManifest();
        if (args[0] == "plan") { Console.WriteLine($"Prepared {cases.Length} inputs, {repetitions} repetitions; no networking or inference."); return 0; }
        var services = new ServiceCollection();
        services.AddLlmProviders(configuration);
        services.AddOptions<DecisionOptions>().Configure(value => configuration.GetSection("Decision").Bind(value));
        services.AddSingleton<IOptionsMonitor<DecisionOptions>>(new FrozenMonitor<DecisionOptions>(settings));
        services.AddSingleton<IOptionsMonitor<KnowledgeOptions>>(new FrozenMonitor<KnowledgeOptions>(knowledge));
        services.AddSingleton<IDecisionEvaluationService, DecisionEvaluationService>();
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ILocalDecisionClient>();
        var execution = new EvaluationExecution(provider.GetRequiredService<IDecisionEvaluationService>(), client, local, settings);
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = TimeSpan.FromSeconds(10) };
        var observations = new List<EvaluationObservation>();
        var snapshots = new List<object>();
        try
        {
            string? nativeDigest = null;
            if (backends.Contains("native", StringComparer.Ordinal))
            {
                var nativeModels = await client.GetModelIdentitiesAsync(settings.DecisionEndpoint ?? local.Endpoint, token);
                nativeDigest = nativeModels.TryGetValue(settings.DecisionModel, out var installedNative) ? installedNative
                    : throw new InvalidOperationException("Dedicated model is not installed.");
                manifest["nativeModelDigest"] = nativeDigest;
            }
            var generalIdentity = await GeneralModelIdentity.ResolveAsync(http, local, token);
            var generalDigest = generalIdentity.Digest;
            manifest["generalModelDigest"] = generalDigest;
            manifest["generalModelMetadata"] = generalIdentity.Metadata;
            manifest["generalIdentityPolicy"] = "Resolve the /api/show runner against matching /api/tags entries; reject ambiguous identities and changes before/after every general call.";
            manifest["ollamaVersion"] = await http.GetFromJsonAsync<JsonElement>(new Uri(new Uri(local.Endpoint), "/api/version"), token);
            SaveManifest();
            foreach (var backend in backends)
            {
                snapshots.Add(await SnapshotAsync(backend + "-before"));
                for (var repeat = 1; repeat <= repetitions; repeat++)
                {
                    foreach (var row in cases)
                    {
                        token.ThrowIfCancellationRequested();
                        bool? cold = null;
                        if (backend == "llm")
                        {
                            if ((await GeneralModelIdentity.ResolveAsync(http, local, token)).Digest != generalDigest)
                                throw new InvalidOperationException("General model identity changed during the run; discard this partial comparison.");
                            var ps = await http.GetFromJsonAsync<JsonElement>(new Uri(new Uri(local.Endpoint), "/api/ps"), token);
                            cold = !ps.GetProperty("models").EnumerateArray().Any(m => m.GetProperty("digest").GetString() == generalDigest);
                        }
                        var observation = await execution.EvaluateAsync(row, repeat, backend, cold, token) with
                        {
                            RunId = runId, ConfigurationSha256 = configurationHash,
                            ModelDigest = backend == "native" ? nativeDigest! : backend == "llm" ? generalDigest! : sourceHashes.GetValueOrDefault("ReadinessRuleBaseline.cs", "unrecorded"),
                        };
                        if (backend == "llm" && (await GeneralModelIdentity.ResolveAsync(http, local, token)).Digest != generalDigest)
                            throw new InvalidOperationException("General model identity changed during inference; discard this partial comparison.");
                        observations.Add(observation);
                        await File.AppendAllTextAsync(Path.Combine(directory, "rows.jsonl"), JsonSerializer.Serialize(observation, LineJson) + "\n", CancellationToken.None);
                        Console.WriteLine($"{backend} {row.Id} #{repeat}: {observation.Status}/{observation.Choice ?? "unavailable"} {observation.TotalMs:0} ms");
                    }
                }
                snapshots.Add(await SnapshotAsync(backend + "-after"));
            }
            manifest["completion"] = "completed";
            return 0;
        }
        catch (OperationCanceledException) { manifest["completion"] = "cancelled"; throw; }
        catch (Exception) { manifest["completion"] = "failed"; throw; }
        finally
        {
            manifest["finishedUtc"] = DateTimeOffset.UtcNow;
            manifest["observations"] = observations.Count;
            manifest["residencySnapshots"] = snapshots;
            SaveManifest();
            EvaluationAnalysis.Write(directory, observations, settings.MinConfidencePercent / 100);
            File.WriteAllText(Path.Combine(directory, "criteria-assessment.json"), JsonSerializer.Serialize(criteria!.Assess(observations), Json));
        }

        void SaveManifest() => File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Json));
        async Task<object> SnapshotAsync(string stage)
        {
            try { return new { stage, recordedUtc = DateTimeOffset.UtcNow, models = await http.GetFromJsonAsync<JsonElement>(new Uri(new Uri(local.Endpoint), "/api/ps"), token) }; }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            { return new { stage, unavailable = true }; }
        }
    }

    private static string Required(Dictionary<string, string> flags, string name) => flags.GetValueOrDefault(name) ?? throw new ArgumentException("Missing " + name);

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string Revision()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
        var result = process!.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return result;
    }

    private sealed class FrozenMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;
        public T Get(string? name) => value;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
