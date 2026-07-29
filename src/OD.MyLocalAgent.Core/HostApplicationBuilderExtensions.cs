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

        RegisterOptions(builder);
        ConfigureLogging(builder, configureAdditionalSinks);

        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ISecretStore>(secretStore);
        builder.Services.AddLlmProviders(builder.Configuration);
        builder.Services.AddGuardrails(builder.Configuration);
        builder.Services.AddConnectors(builder.Configuration);
        builder.Services.AddHttpClient(HttpTool.HttpClientName);

        builder.Services.AddSingleton<IPathSandbox, PathSandbox>();
        builder.Services.AddSingleton<FileSystemTool>();
        builder.Services.AddSingleton<IFileSystemTool>(
            static services => services.GetRequiredService<FileSystemTool>());
        builder.Services.AddSingleton<GmailTool>();
        builder.Services.AddSingleton<IGmailTool>(
            static services => services.GetRequiredService<GmailTool>());
        builder.Services.AddSingleton<CalendarTool>();
        builder.Services.AddSingleton<ICalendarTool>(
            static services => services.GetRequiredService<CalendarTool>());
        builder.Services.AddSingleton<DropboxTool>();
        builder.Services.AddSingleton<IDropboxTool>(
            static services => services.GetRequiredService<DropboxTool>());
        builder.Services.AddSingleton<HttpTool>();
        builder.Services.AddSingleton<IHttpTool>(
            static services => services.GetRequiredService<HttpTool>());
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

        builder.Services.AddSingleton<IIncidentLogService, JsonlIncidentLogService>();
        builder.Services.AddSingleton<IIncidentReporter, IncidentReporter>();
        builder.Services.AddSingleton<JsonTaskStore>();
        builder.Services.AddSingleton<ITaskStore>(
            static services => services.GetRequiredService<JsonTaskStore>());
        builder.Services.AddSingleton<ITaskSchedulerService, WindowsTaskSchedulerService>();
        builder.Services.AddSingleton<IMyLocalAgentService, MyLocalAgentService>();
        builder.Services.AddSingleton<IRunnerService, RunnerService>();
        return builder;
    }

    private static void RegisterOptions(IHostApplicationBuilder builder)
    {
        builder.Services
            .AddOptions<SafetyOptions>()
            .Bind(builder.Configuration.GetSection(SafetyOptions.SectionName));
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
                    Enum.TryParse<LogEventLevel>(
                        options.MinimumLevel,
                        ignoreCase: true,
                        out _),
                "Logging minimum level or in-memory capacity is invalid.");
    }

    private static void ConfigureLogging(
        IHostApplicationBuilder builder,
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

        Directory.CreateDirectory(LocalAppPaths.LogsDirectory);
        var loggerConfiguration = new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(LocalAppPaths.LogsDirectory, "application-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .WriteTo.Sink(inMemorySink);
        configureAdditionalSinks?.Invoke(loggerConfiguration);
        builder.Services.AddSerilog(loggerConfiguration.CreateLogger(), dispose: true);
    }
}
