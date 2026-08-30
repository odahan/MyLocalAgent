using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using Serilog;
using Serilog.Events;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides the shared Core composition root for the UI and runner.
/// </summary>
public static class HostApplicationBuilderExtensions
{
    /// <summary>
    /// Applies layered configuration, Serilog, providers, connectors, and all Core services.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <param name="configureAdditionalSinks">
    /// An optional sink configurator, used by the runner to add its console sink.
    /// </param>
    /// <returns>The supplied builder.</returns>
    public static IHostApplicationBuilder AddODServices(
        this IHostApplicationBuilder builder,
        Action<LoggerConfiguration>? configureAdditionalSinks = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var secretStore = new DpapiSecretStore();
        var secrets = secretStore.ReadAllAsync(CancellationToken.None)
            .GetAwaiter()
            .GetResult()
            .Where(pair => SecretKeys.All.Contains(pair.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(
                static pair => pair.Key,
                static pair => (string?)pair.Value,
                StringComparer.OrdinalIgnoreCase);

        builder.Configuration.Sources.Clear();
        builder.Configuration
            .AddJsonFile(
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                optional: false,
                reloadOnChange: false)
            .AddJsonFile(
                LocalAppPaths.UserSettingsPath,
                optional: true,
                reloadOnChange: true)
            .AddInMemoryCollection(secrets);

        var workspacePaths = new WorkspacePaths(
            builder.Configuration[$"{FileSystemOptions.SectionName}:RootPath"]
            ?? throw new InvalidOperationException(
                "FileSystem:RootPath is required."));
        workspacePaths.EnsureDirectories();

        RegisterOptions(builder);
        ConfigureLogging(builder, workspacePaths, configureAdditionalSinks);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<IExecutionDebugTrace, NullExecutionDebugTrace>();
        builder.Services.AddSingleton(workspacePaths);
        builder.Services.AddSingleton<ISecretStore>(secretStore);
        builder.Services.AddLlmProviders(builder.Configuration);
        builder.Services.AddGuardrails(builder.Configuration);
        builder.Services.AddConnectors(builder.Configuration);
        builder.Services.AddHttpClient(HttpTool.HttpClientName);

        builder.Services.AddSingleton<IPathSandbox, PathSandbox>();
        builder.Services.AddSingleton<IRecycleBinService, WindowsRecycleBinService>();
        builder.Services.AddSingleton<IResearchTranslationService, ResearchTranslationService>();
        builder.Services.AddSingleton<SqliteKnowledgeStore>();
        builder.Services.AddSingleton<IKnowledgeDocumentExtractor, KnowledgeDocumentExtractor>();
        builder.Services.AddSingleton<KnowledgeChunker>();
        builder.Services.AddSingleton<IKnowledgeEmbeddingGeneratorFactory, KnowledgeEmbeddingGeneratorFactory>();
        builder.Services.AddSingleton<IKnowledgeEmbeddingService, KnowledgeEmbeddingService>();
        builder.Services.AddSingleton<IProposalMemoryService, ProposalMemoryService>();
        builder.Services.AddSingleton<IKnowledgeSearchService, KnowledgeSearchService>();
        builder.Services.AddSingleton<IKnowledgeLibraryService, KnowledgeLibraryService>();

        var catalog = ToolCatalog.Default;
        builder.Services.AddSingleton<IToolCatalog>(catalog);
        builder.Services.AddSingleton<IToolTargetActivator, ToolTargetActivator>();

        var registeredTypes = new HashSet<Type>();
        foreach (var group in catalog.Groups)
        {
            var def = catalog.Definitions.FirstOrDefault(d => d.ToolGroup == group.Name);
            if (def is null || registeredTypes.Contains(def.DeclaringType))
            {
                continue;
            }

            registeredTypes.Add(def.DeclaringType);
            var toolType = def.DeclaringType;
            var groupAttr = toolType.GetCustomAttribute<ToolGroupAttribute>();

            if (def.Activation == ToolActivationKind.Singleton)
            {
                builder.Services.AddSingleton(toolType);
            }
            else if (groupAttr?.ServiceContract is not null && groupAttr.RegisterTasklessProjection)
            {
                builder.Services.AddSingleton(toolType, sp =>
                    sp.GetRequiredService<IToolTargetActivator>().CreateTarget(toolType, ToolExecutionContext.Taskless));
            }

            if (groupAttr?.ServiceContract is not null)
            {
                var serviceContract = groupAttr.ServiceContract;
                builder.Services.AddSingleton(serviceContract, sp => sp.GetRequiredService(toolType));
            }
        }

        builder.Services.AddSingleton<IToolRegistry, ToolRegistry>();

        builder.Services.AddSingleton<SummarizeTextSkill>();
        builder.Services.AddSingleton<ExtractActionItemsSkill>();
        builder.Services.AddSingleton<ClassifyEmailSkill>();
        builder.Services.AddSingleton<ISkill>(
            static services => services.GetRequiredService<SummarizeTextSkill>());
        builder.Services.AddSingleton<ISkill>(
            static services => services.GetRequiredService<ExtractActionItemsSkill>());
        builder.Services.AddSingleton<ISkill>(
            static services => services.GetRequiredService<ClassifyEmailSkill>());

        builder.Services.AddSingleton<IIncidentLogService>(
            static services => new JsonlIncidentLogService(
                services.GetRequiredService<WorkspacePaths>().IncidentsPath,
                services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<IIncidentReporter, IncidentReporter>();
        builder.Services.AddSingleton<JsonTaskStore>(
            static services => new JsonTaskStore(
                services.GetRequiredService<WorkspacePaths>().TasksDirectory,
                services.GetRequiredService<TimeProvider>(),
                services.GetRequiredService<WorkspacePaths>().TaskArchivesDirectory));
        builder.Services.AddSingleton<ITaskStore>(
            static services => services.GetRequiredService<JsonTaskStore>());
        builder.Services.AddSingleton<SqliteDirectChatStore>(
            static services =>
            {
                var options = services
                    .GetRequiredService<Microsoft.Extensions.Options.IOptions<DirectChatOptions>>()
                    .Value;
                return new SqliteDirectChatStore(
                    services.GetRequiredService<WorkspacePaths>().DirectChatDatabasePath,
                    services.GetRequiredService<TimeProvider>(),
                    options.MaximumMediaBytes,
                    options.MaximumConversationMediaBytes);
            });
        builder.Services.AddSingleton<IDirectChatStore>(
            static services => services.GetRequiredService<SqliteDirectChatStore>());
        builder.Services.AddSingleton<ITokenEstimator, ConservativeTokenEstimator>();
        builder.Services.AddSingleton<IDirectChatContextBuilder, DirectChatContextBuilder>();
        builder.Services.AddSingleton<IDirectChatHistoryCompressor, DirectChatHistoryCompressor>();
        builder.Services.AddSingleton<IDirectChatMediaFileService, DirectChatMediaFileService>();
        builder.Services.AddSingleton<ITaskSchedulerService, WindowsTaskSchedulerService>();
        builder.Services.AddSingleton<IMyLocalAgentService, MyLocalAgentService>();
        builder.Services.AddSingleton<IRunnerService, RunnerService>();
        builder.Services.AddSingleton<IRunLogCatalog>(
            static services => new RunLogCatalog(
                services.GetRequiredService<WorkspacePaths>().RunLogsDirectory));
        return builder;
    }

    private static void RegisterOptions(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<KnowledgeOptions>()
            .Bind(builder.Configuration.GetSection(KnowledgeOptions.SectionName))
            .Validate(KnowledgeOptions.IsValid, "Knowledge settings are invalid.")
            .ValidateOnStart();
        builder.Services
            .AddOptions<DirectChatOptions>()
            .Bind(builder.Configuration.GetSection(DirectChatOptions.SectionName))
            .Validate(DirectChatOptions.IsValid, "Direct Chat settings are invalid.")
            .ValidateOnStart();
            builder.Services
                .AddOptions<FileContentOptions>()
                .Bind(builder.Configuration.GetSection(FileContentOptions.SectionName))
                .Validate(FileContentOptions.IsValid, "FileContent settings are invalid.")
                .ValidateOnStart();
        builder.Services
            .AddOptions<SQLiteOptions>()
            .Bind(builder.Configuration.GetSection(SQLiteOptions.SectionName))
            .Validate(SQLiteOptions.IsValid, "SQLite settings are invalid.");
        builder.Services
            .AddOptions<FileSystemOptions>()
            .Bind(builder.Configuration.GetSection(FileSystemOptions.SectionName))
            .Validate(
                static options =>
                    !string.IsNullOrWhiteSpace(options.RootPath) &&
                    Path.IsPathRooted(options.RootPath) &&
                    options.MaxReadBytes > 0,
                "FileSystem:RootPath must be absolute and MaxReadBytes must be positive.");
        builder.Services
            .AddOptions<HttpOptions>()
            .Bind(builder.Configuration.GetSection(HttpOptions.SectionName))
            .Validate(
                static options =>
                    options.TimeoutSeconds > 0 &&
                    options.MaxResponseCharacters > 0 &&
                    !string.IsNullOrWhiteSpace(options.UserAgent) &&
                    (options.AllowedHosts ?? []).All(
                        static host =>
                            !string.IsNullOrWhiteSpace(host) &&
                            !host.Contains('/')),
                "HTTP timeout, response cap, user agent, or allowed hosts are invalid.");
        builder.Services
            .AddOptions<SchedulerOptions>()
            .Bind(builder.Configuration.GetSection(SchedulerOptions.SectionName))
            .Validate(
                static options => !string.IsNullOrWhiteSpace(options.TaskFolder),
                "Scheduler:TaskFolder is required.");
        builder.Services
            .AddOptions<LoggingOptions>()
            .Bind(builder.Configuration.GetSection(LoggingOptions.SectionName))
            .Validate(
                static options =>
                    options.InMemoryCapacity > 0 &&
                    options.MaxRunLogsPerTask > 0 &&
                    Enum.TryParse<LogEventLevel>(
                        options.MinimumLevel,
                        ignoreCase: true,
                        out _),
                "Logging minimum level, in-memory capacity, or run-log retention is invalid.");
    }

    private static void ConfigureLogging(
        IHostApplicationBuilder builder,
        WorkspacePaths workspacePaths,
        Action<LoggerConfiguration>? configureAdditionalSinks)
    {
        var options = builder.Configuration
            .GetSection(LoggingOptions.SectionName)
            .Get<LoggingOptions>() ?? new LoggingOptions();
        var minimumLevel = Enum.TryParse<LogEventLevel>(
            options.MinimumLevel,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : LogEventLevel.Information;
        var inMemorySink = new InMemoryLogSink(options.InMemoryCapacity);
        builder.Services.AddSingleton(inMemorySink);
        builder.Services.AddSingleton<IInMemoryLogSink>(inMemorySink);

        Directory.CreateDirectory(workspacePaths.ApplicationLogsDirectory);
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(workspacePaths.ApplicationLogsDirectory, "application-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .WriteTo.Sink(inMemorySink);
        configureAdditionalSinks?.Invoke(loggerConfiguration);
        builder.Services.AddSerilog(loggerConfiguration.CreateLogger(), dispose: true);
    }
}
