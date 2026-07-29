using System.Text.Json;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Executes workflow steps deterministically in declaration order.
/// </summary>
public sealed class SequentialWorkflowEngine : IWorkflowEngine
{
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly IToolRegistry _toolRegistry;
    private readonly IReadOnlyDictionary<string, ISkill> _skills;
    private readonly IMyLocalAgentService _agentService;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SequentialWorkflowEngine> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SequentialWorkflowEngine"/> class.
    /// </summary>
    public SequentialWorkflowEngine(
        IToolRegistry toolRegistry,
        IEnumerable<ISkill> skills,
        IMyLocalAgentService agentService,
        TimeProvider timeProvider,
        ILogger<SequentialWorkflowEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(toolRegistry);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(agentService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _toolRegistry = toolRegistry;
        _skills = skills.ToDictionary(
            static skill => skill.Name,
            StringComparer.OrdinalIgnoreCase);
        _agentService = agentService;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WorkflowResult> ExecuteAsync(
        ScheduledTask task,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        if (task.Workflow is null || task.Workflow.Count == 0)
        {
            throw new ArgumentException(
                "Sequential workflow execution requires at least one step.",
                nameof(task));
        }

        var startedUtc = _timeProvider.GetUtcNow();
        var context = new WorkflowContext();
        var results = new List<WorkflowStepResult>(task.Workflow.Count);

        foreach (var definition in task.Workflow)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var step = WorkflowStepFactory.Create(definition, _toolRegistry);
            var policy = step.OnError ?? task.ErrorPolicy;
            var result = await ExecuteWithPolicyAsync(
                step,
                policy,
                task,
                context,
                cancellationToken).ConfigureAwait(false);
            results.Add(result);

            if (result.Status == WorkflowStepStatus.Success)
            {
                context.RecordOutput(step.Id, result.Output ?? string.Empty);
                continue;
            }

            if (policy != WorkflowErrorPolicy.ContinueOnError)
            {
                break;
            }
        }

        var endedUtc = _timeProvider.GetUtcNow();
        var success = results.All(
            static result => result.Status == WorkflowStepStatus.Success);
        var finalText = results.Count == 0
            ? string.Empty
            : results[^1].Output ?? string.Empty;
        var errorMessage = results
            .LastOrDefault(
                static result => result.Status == WorkflowStepStatus.Failure)
            ?.ErrorMessage;
        return new WorkflowResult(
            success,
            results,
            finalText,
            startedUtc,
            endedUtc,
            errorMessage);
    }

    private async Task<WorkflowStepResult> ExecuteWithPolicyAsync(
        WorkflowStep step,
        WorkflowErrorPolicy policy,
        ScheduledTask task,
        WorkflowContext context,
        CancellationToken cancellationToken)
    {
        var startedUtc = _timeProvider.GetUtcNow();
        var startedTimestamp = _timeProvider.GetTimestamp();
        var attempts = 0;

        _logger.LogInformation(
            "Workflow step {StepId} ({StepAlias}) started.",
            step.Id,
            step.Alias);

        while (true)
        {
            attempts++;
            try
            {
                var resolvedArguments =
                    WorkflowPlaceholderResolver.ResolveArguments(
                        step.Args,
                        task,
                        context,
                        step.Id);
                var output = await ExecuteStepAsync(
                    step,
                    resolvedArguments,
                    task,
                    context,
                    cancellationToken).ConfigureAwait(false);
                var duration = _timeProvider.GetElapsedTime(startedTimestamp);
                var outputSize = System.Text.Encoding.UTF8.GetByteCount(output);
                _logger.LogInformation(
                    "Workflow step {StepId} ({StepAlias}) completed in {DurationMs} ms with status {Status}, output size {OutputSizeBytes} bytes, and {Attempts} attempt(s).",
                    step.Id,
                    step.Alias,
                    duration.TotalMilliseconds,
                    WorkflowStepStatus.Success,
                    outputSize,
                    attempts);
                return new WorkflowStepResult(
                    step.Id,
                    step.Alias,
                    WorkflowStepStatus.Success,
                    output,
                    null,
                    null,
                    startedUtc,
                    duration,
                    attempts);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException)
            {
                var retryNumber = attempts;
                if (policy == WorkflowErrorPolicy.Retry &&
                    retryNumber <= step.RetryCount)
                {
                    var delay = ComputeBackoff(step, retryNumber);
                    _logger.LogWarning(
                        exception,
                        "Workflow step {StepId} ({StepAlias}) attempt {Attempt} failed. Retrying in {BackoffSeconds} seconds.",
                        step.Id,
                        step.Alias,
                        attempts,
                        delay.TotalSeconds);
                    await Task.Delay(
                        delay,
                        _timeProvider,
                        cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var duration = _timeProvider.GetElapsedTime(startedTimestamp);
                _logger.LogWarning(
                    exception,
                    "Workflow step {StepId} ({StepAlias}) completed in {DurationMs} ms with status {Status}, output size {OutputSizeBytes} bytes, and {Attempts} attempt(s).",
                    step.Id,
                    step.Alias,
                    duration.TotalMilliseconds,
                    WorkflowStepStatus.Failure,
                    0,
                    attempts);
                return new WorkflowStepResult(
                    step.Id,
                    step.Alias,
                    WorkflowStepStatus.Failure,
                    null,
                    exception.Message,
                    exception,
                    startedUtc,
                    duration,
                    attempts);
            }
        }
    }

    private async Task<string> ExecuteStepAsync(
        WorkflowStep step,
        JsonElement resolvedArguments,
        ScheduledTask task,
        WorkflowContext context,
        CancellationToken cancellationToken)
    {
        switch (step)
        {
            case ToolStep:
                return await _toolRegistry.InvokeAsync(
                    step.Alias,
                    resolvedArguments,
                    task.Tools,
                    cancellationToken).ConfigureAwait(false);

            case SkillStep skillStep:
                var skill = _skills.TryGetValue(skillStep.SkillName, out var resolvedSkill)
                    ? resolvedSkill
                    : throw new WorkflowValidationException(
                        $"Workflow skill '{skillStep.SkillName}' is not registered.");
                var text = ReadOptionalString(resolvedArguments, "text") ??
                    context.PreviousOutput ??
                    task.Prompt;
                var skillOutput = await skill.ExecuteAsync(
                    text,
                    cancellationToken).ConfigureAwait(false);
                return JsonSerializer.Serialize(
                    new { text = skillOutput },
                    SerializerOptions);

            case PromptStep:
                var prompt = ReadRequiredString(resolvedArguments, "prompt", step.Alias);
                var agentResult = await _agentService.RunPromptAsync(
                    prompt,
                    task.Tools,
                    cancellationToken).ConfigureAwait(false);
                return agentResult.Text;

            default:
                throw new InvalidOperationException(
                    $"Unsupported workflow step type '{step.GetType().Name}'.");
        }
    }

    private static string? ReadOptionalString(
        JsonElement arguments,
        string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element))
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : throw new ArgumentException(
                $"Workflow argument '{propertyName}' must be a string.");
    }

    private static string ReadRequiredString(
        JsonElement arguments,
        string propertyName,
        string alias) =>
        ReadOptionalString(arguments, propertyName) ??
        throw new ArgumentException(
            $"Workflow alias '{alias}' requires string argument '{propertyName}'.");

    private static TimeSpan ComputeBackoff(
        WorkflowStep step,
        int retryNumber)
    {
        var maximumDelay =
            TimeSpan.FromMilliseconds(uint.MaxValue - 1d);
        var multiplier = Math.Pow(2, retryNumber - 1);
        var seconds = step.RetryBackoffSeconds * multiplier;
        if (double.IsInfinity(seconds) || seconds > maximumDelay.TotalSeconds)
        {
            return maximumDelay;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
