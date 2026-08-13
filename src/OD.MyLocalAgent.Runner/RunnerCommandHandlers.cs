using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Workflows;

namespace OD.MyLocalAgent.Runner;

internal static class RunnerCommandHandlers
{
    private static readonly JsonSerializerOptions IndentedJsonOptions =
        new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public static async Task<int> RunAsync(
        IServiceProvider services,
        string taskId,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        using var executionMutex = new TaskExecutionMutex(taskId);
        if (!executionMutex.TryAcquire())
        {
            output.WriteLine(
                $"Task '{taskId}' is already running in another process.");
            return RunnerExitCodes.TaskFailure;
        }

        var runLogHolder = services.GetService<RunLogSessionHolder>();
        var runLogSession = await TryStartRunLogAsync(
            services,
            taskId,
            cancellationToken).ConfigureAwait(false);
        if (runLogHolder is not null)
        {
            runLogHolder.Session = runLogSession;
        }

        var exitCode = RunnerExitCodes.TaskFailure;
        try
        {
            exitCode = await RunCoreAsync(
                services,
                taskId,
                output,
                error,
                cancellationToken).ConfigureAwait(false);
            return exitCode;
        }
        finally
        {
            if (runLogHolder is not null)
            {
                runLogHolder.Session = null;
            }

            runLogSession?.Complete(exitCode);
            runLogSession?.Dispose();
        }
    }

    private static async Task<RunLogSession?> TryStartRunLogAsync(
        IServiceProvider services,
        string taskId,
        CancellationToken cancellationToken)
    {
        try
        {
            return RunLogSession.Start(
                taskId,
                services
                    .GetRequiredService<IOptions<LoggingOptions>>()
                    .Value.MaxRunLogsPerTask,
                services.GetRequiredService<TimeProvider>());
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            // Run logging is best-effort (§19.3); the task still executes.
            _ = await AppExceptionHandlers.HandleAsync(
                exception,
                taskId,
                cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    private static async Task<int> RunCoreAsync(
        IServiceProvider services,
        string taskId,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var taskStore = services.GetRequiredService<ITaskStore>();
        var task = await taskStore.GetAsync(
            taskId,
            cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            error.WriteLine($"Task '{taskId}' was not found.");
            return RunnerExitCodes.TaskNotFound;
        }

        WriteModelInfoToRunLog(services, task);

        if (!task.Enabled)
        {
            error.WriteLine($"Task '{taskId}' is disabled.");
            return RunnerExitCodes.TaskFailure;
        }

        using var modelScope = ModelExecutionScope.Push(task.Model);
        var outcome = await ExecuteAndWriteReportAsync(
            services,
            task,
            executeWorkflow: task.Workflow.Count > 0,
            output,
            cancellationToken).ConfigureAwait(false);
        output.WriteLine(
            $"Task '{task.Id}' finished with status " +
            $"{(outcome.Result.Success ? "Success" : "Failure")}.");
        output.WriteLine($"Task execution report: {outcome.Report.ReportPath}");

        var scheduler = services.GetRequiredService<ITaskSchedulerService>();
        await scheduler.RegisterAsync(task, cancellationToken).ConfigureAwait(false);
        output.WriteLine("Next occurrence registered.");

        return outcome.Result.Success
            ? RunnerExitCodes.Success
            : await ReportFailureAsync(
                task,
                outcome.Error,
                error,
                cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> ListAsync(
        IServiceProvider services,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var taskStore = services.GetRequiredService<ITaskStore>();
        var scheduler = services.GetRequiredService<ITaskSchedulerService>();
        var tasks = await taskStore.GetAllAsync(
            cancellationToken).ConfigureAwait(false);
        var rows = new List<string[]>(tasks.Count);
        foreach (var task in tasks)
        {
            var nextRun = await scheduler.GetNextRunAsync(
                task.Id,
                cancellationToken).ConfigureAwait(false);
            rows.Add(
            [
                task.Id,
                task.Name,
                task.Schedule,
                task.Enabled ? "Yes" : "No",
                nextRun?.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)
                    ?? "-",
            ]);
        }

        WriteTable(
            output,
            ["Id", "Name", "Schedule", "Enabled", "Next run"],
            rows);
        if (rows.Count == 0)
        {
            output.WriteLine("(no tasks)");
        }

        return RunnerExitCodes.Success;
    }

    public static async Task<int> TestPromptAsync(
        IServiceProvider services,
        string taskId,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var taskStore = services.GetRequiredService<ITaskStore>();
        var task = await taskStore.GetAsync(
            taskId,
            cancellationToken).ConfigureAwait(false);
        if (task is null)
        {
            error.WriteLine($"Task '{taskId}' was not found.");
            return RunnerExitCodes.TaskNotFound;
        }

        if (string.IsNullOrWhiteSpace(task.Prompt))
        {
            error.WriteLine($"Task '{taskId}' does not define a prompt.");
            return RunnerExitCodes.TaskFailure;
        }

        var outcome = await ExecuteAndWriteReportAsync(
            services,
            task,
            executeWorkflow: false,
            output,
            cancellationToken).ConfigureAwait(false);
        output.Write(outcome.Report.Markdown);
        return outcome.Result.Success
            ? RunnerExitCodes.Success
            : await ReportFailureAsync(
                task,
                outcome.Error,
                error,
                cancellationToken).ConfigureAwait(false);
    }

    public static async Task<int> TestToolAsync(
        IServiceProvider services,
        string alias,
        string argumentsJson,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        var registry = services.GetRequiredService<IToolRegistry>();
        if (!registry.TryGetDefinition(alias, out var definition) ||
            definition is null ||
            !definition.IsAgentTool)
        {
            error.WriteLine($"Unknown directly invocable Tool Map alias '{alias}'.");
            return RunnerExitCodes.TaskFailure;
        }

        using var document = JsonDocument.Parse(argumentsJson);
        var arguments = document.RootElement.Clone();
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var startedTimestamp = timeProvider.GetTimestamp();
        try
        {
            var result = await registry.InvokeAsync(
                alias,
                arguments,
                [definition.ToolGroup],
                cancellationToken).ConfigureAwait(false);
            output.WriteLine(PrettyPrintJson(result));
            output.WriteLine(
                $"Duration: {timeProvider.GetElapsedTime(startedTimestamp).TotalMilliseconds:F0} ms");
            return RunnerExitCodes.Success;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException &&
            RunnerExceptionClassifier.Find<SafetyException>(exception) is { } safetyException)
        {
            error.WriteLine(safetyException.Message);
            error.WriteLine(
                $"Blocked by safety configuration. Enable '{safetyException.RequiredFlag}' to allow this operation.");
            return RunnerExitCodes.TaskFailure;
        }
    }

    public static async Task<int> ResyncSchedulesAsync(
        IServiceProvider services,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var taskStore = services.GetRequiredService<ITaskStore>();
        var scheduler = services.GetRequiredService<ITaskSchedulerService>();
        var tasks = await taskStore.GetAllAsync(
            cancellationToken).ConfigureAwait(false);
        var result = await scheduler.ResyncAllAsync(
            cancellationToken).ConfigureAwait(false);
        var failures = new HashSet<string>(
            result.Failures,
            StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            var status = failures.Contains(task.Id)
                ? "Failed"
                : task.Enabled
                    ? "Registered/refreshed"
                    : "Unregistered";
            output.WriteLine($"{task.Id}: {status}");
        }

        foreach (var failure in result.Failures.Where(
                     static id => id.StartsWith('<') && id.EndsWith('>')))
        {
            output.WriteLine($"{failure}: Failed");
        }

        output.WriteLine(
            $"Summary: {result.Registered} registered/refreshed, " +
            $"{result.Unregistered} unregistered, " +
            $"{result.Failures.Count} failure(s).");
        return result.Failures.Count == 0
            ? RunnerExitCodes.Success
            : RunnerExitCodes.TaskFailure;
    }

    private static async Task<ExecutionOutcome> ExecuteAndWriteReportAsync(
        IServiceProvider services,
        ScheduledTask task,
        bool executeWorkflow,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        using var modelScope = ModelExecutionScope.Push(task.Model);
        var reportWriter = services.GetRequiredService<IReportWriter>();
        var configuration = services.GetRequiredService<IConfiguration>();
        var providerName = configuration["AI:Provider"] ?? "Unknown";
        WorkflowResult result;
        Exception? error = null;

        TaskTimingCollector.Current.Value = new TaskTimingCollector();

        await WarmUpOllamaModelAsync(
            services,
            task,
            output,
            cancellationToken).ConfigureAwait(false);

        if (executeWorkflow)
        {
            var engine = services.GetRequiredService<IWorkflowEngine>();
            result = await engine.ExecuteAsync(
                task,
                context => output.WriteLine(RunnerContextProtocol.Encode(context)),
                cancellationToken).ConfigureAwait(false);
            error = result.Steps
                .LastOrDefault(
                    static step => step.Status == WorkflowStepStatus.Failure)
                ?.Error;
        }
        else
        {
            var promptOutcome = await ExecutePromptOnlyAsync(
                services,
                task,
                output,
                cancellationToken).ConfigureAwait(false);
            result = promptOutcome.Result;
            providerName = promptOutcome.ProviderName;
            error = promptOutcome.Error;
        }

        EmitTimingReport(services, output, result);

        var report = await reportWriter.WriteAsync(
            task,
            result,
            providerName,
            cancellationToken).ConfigureAwait(false);
        return new ExecutionOutcome(result, report, error);
    }

    private static async Task WarmUpOllamaModelAsync(
        IServiceProvider services,
        ScheduledTask task,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var aiOptions = services
            .GetRequiredService<IOptionsMonitor<AIOptions>>()
            .CurrentValue;
        if (!string.Equals(
                aiOptions.Provider,
                AIOptions.OllamaProvider,
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var warmup = services.GetService<ILlmModelWarmup>();
        if (warmup is null)
        {
            return;
        }

        var policy = GenerationPolicyResolver.Resolve(
            aiOptions.GenerationDefaults,
            task.GenerationOptions);
        output.WriteLine("Warming up the configured Ollama model before task execution.");
        await warmup.WarmUpAsync(policy, cancellationToken).ConfigureAwait(false);
        output.WriteLine("Ollama model warm-up completed.");
    }

    private static void EmitTimingReport(
        IServiceProvider services,
        TextWriter output,
        WorkflowResult result)
    {
        var entries = TaskTimingCollector.Current.Value?.GetEntries()
            ?? [];
        var llmUsageEntries = TaskTimingCollector.Current.Value?.GetLlmUsageEntries()
            ?? [];
        var timingReport = TaskTimingReport.Build(
            result.StartedUtc,
            result.EndedUtc,
            entries,
            llmUsageEntries);
        // output is a RunLogTeeWriter: writing to it already mirrors each line
        // to the active RunLogSession, so no explicit WriteTimingReport call needed.
        output.Write(timingReport);
    }

    private static async Task<PromptExecutionOutcome> ExecutePromptOnlyAsync(
        IServiceProvider services,
        ScheduledTask task,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        var timeProvider = services.GetRequiredService<TimeProvider>();
        var startedUtc = timeProvider.GetUtcNow();
        var localNow = timeProvider.GetLocalNow();
        var resolution = AgentPromptPlaceholderResolver.Resolve(
            task.Prompt,
            task.Id,
            task.Name,
            localNow);
        var warningMessage = AgentPromptPlaceholderWarning.FormatSummary(resolution);
        IReadOnlyList<ExecutionWarning> warnings = warningMessage.Length == 0
            ? []
            :
            [
                new ExecutionWarning(
                    AgentPromptPlaceholderWarning.Code,
                    warningMessage,
                    resolution.TotalUnresolvedCount),
            ];
        if (warningMessage.Length > 0)
        {
            output.WriteLine(warningMessage);
        }

        var providerName = services.GetRequiredService<IConfiguration>()["AI:Provider"] ??
            "Unknown";
        try
        {
            var agent = services.GetRequiredService<IMyLocalAgentService>();
            var generationPolicy = GenerationPolicyResolver.Resolve(
                services
                    .GetRequiredService<IOptionsMonitor<AIOptions>>()
                    .CurrentValue.GenerationDefaults,
                task.GenerationOptions);
            var agentResult = await agent.RunPromptAsync(
                resolution.ResolvedPrompt,
                task.Tools,
                ToolExecutionContext.FromTask(task),
                generationPolicy,
                cancellationToken).ConfigureAwait(false);
            providerName = agentResult.ProviderName;
            return new PromptExecutionOutcome(
                new WorkflowResult(
                    true,
                    [],
                    agentResult.Text,
                    startedUtc,
                    timeProvider.GetUtcNow())
                {
                    Warnings = warnings,
                },
                providerName,
                null);
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException)
        {
            return new PromptExecutionOutcome(
                new WorkflowResult(
                    false,
                    [],
                    string.Empty,
                    startedUtc,
                    timeProvider.GetUtcNow(),
                    exception.Message)
                {
                    Warnings = warnings,
                },
                providerName,
                exception);
        }
    }

    private static async Task<int> ReportFailureAsync(
        ScheduledTask task,
        Exception? exception,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        exception ??= new InvalidOperationException(
            $"Task '{task.Id}' failed without an exception detail.");
        var existingAgentIncident =
            RunnerExceptionClassifier.Find<AgentExecutionException>(exception);
        var correlationId = existingAgentIncident?.CorrelationId ??
            await AppExceptionHandlers.HandleAsync(
                exception,
                task.Id,
                cancellationToken).ConfigureAwait(false);
        error.WriteLine(
            $"Task '{task.Id}' failed: {exception.Message} Incident: {correlationId}.");
        return RunnerExceptionClassifier.GetExitCode(exception);
    }

    private static void WriteModelInfoToRunLog(
        IServiceProvider services,
        ScheduledTask task)
    {
        var aiOptions = services
            .GetRequiredService<IOptionsMonitor<AIOptions>>()
            .CurrentValue;
        var generationPolicy = GenerationPolicyResolver.Resolve(
            aiOptions.GenerationDefaults,
            task.GenerationOptions);
        var modelName = task.Model ?? (string.Equals(
            aiOptions.Provider,
            AIOptions.OpenAIProvider,
            StringComparison.OrdinalIgnoreCase)
            ? services
                .GetRequiredService<IOptionsMonitor<OpenAIOptions>>()
                .CurrentValue.Model
            : services
                .GetRequiredService<IOptionsMonitor<OllamaOptions>>()
                .CurrentValue.Model);
        var modelInfo = new ModelPromptPlaceholderContext(
            modelName,
            generationPolicy).ModelInfo;
        services.GetService<RunLogSessionHolder>()?.Session?.WriteModelInfo(modelInfo);
    }

    private static string PrettyPrintJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
            return JsonSerializer.Serialize(
                document.RootElement,
                IndentedJsonOptions);
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static void WriteTable(
        TextWriter output,
        IReadOnlyList<string> headers,
        IReadOnlyList<string[]> rows)
    {
        var widths = headers.Select(static header => header.Length).ToArray();
        foreach (var row in rows)
        {
            for (var index = 0; index < widths.Length; index++)
            {
                widths[index] = Math.Max(widths[index], row[index].Length);
            }
        }

        output.WriteLine(FormatRow(headers, widths));
        output.WriteLine(
            string.Join(
                "-+-",
                widths.Select(static width => new string('-', width))));
        foreach (var row in rows)
        {
            output.WriteLine(FormatRow(row, widths));
        }
    }

    private static string FormatRow(
        IReadOnlyList<string> values,
        IReadOnlyList<int> widths) =>
        string.Join(
            " | ",
            values.Select(
                (value, index) =>
                    value.PadRight(widths[index], ' ')));

    private sealed record ExecutionOutcome(
        WorkflowResult Result,
        ReportWriteResult Report,
        Exception? Error);

    private sealed record PromptExecutionOutcome(
        WorkflowResult Result,
        string ProviderName,
        Exception? Error);
}
