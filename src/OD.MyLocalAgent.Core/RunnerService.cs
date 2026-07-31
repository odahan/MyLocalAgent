using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Implements the UI Run-now child-process path.
/// </summary>
public sealed class RunnerService : IRunnerService
{
    private readonly IOptions<SchedulerOptions> _options;
    private readonly ILogger<RunnerService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunnerService"/> class.
    /// </summary>
    public RunnerService(
        IOptions<SchedulerOptions> options,
        ILogger<RunnerService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string RunnerPath => RunnerPathResolver.Resolve(_options.Value);

    /// <inheritdoc />
    public async Task<int> RunTaskAsync(
        string taskId,
        Action<string>? outputLine,
        Action<string>? contextUpdated,
        CancellationToken cancellationToken)
    {
        ValidateId(taskId);
        var startInfo = new ProcessStartInfo
        {
            FileName = RunnerPath,
            WorkingDirectory = Path.GetDirectoryName(RunnerPath)!,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--task");
        startInfo.ArgumentList.Add(taskId);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("The runner process could not be started.");
        }

        _logger.LogInformation(
            "Started runner process {ProcessId} for task {TaskId}.",
            process.Id,
            taskId);

        using var cancellationRegistration = cancellationToken.Register(
            static state =>
            {
                var child = (Process)state!;
                try
                {
                    if (!child.HasExited)
                    {
                        child.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            },
            process);

        var stdoutTask = PumpAsync(
            process.StandardOutput,
            outputLine,
            contextUpdated,
            cancellationToken);
        var stderrTask = PumpAsync(
            process.StandardError,
            outputLine,
            contextUpdated,
            cancellationToken);
        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        _logger.LogInformation(
            "Runner process for task {TaskId} exited with code {ExitCode}.",
            taskId,
            process.ExitCode);
        return process.ExitCode;
    }

    private static async Task PumpAsync(
        StreamReader reader,
        Action<string>? outputLine,
        Action<string>? contextUpdated,
        CancellationToken cancellationToken)
    {
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (RunnerContextProtocol.TryDecode(line, out var contextJson))
            {
                contextUpdated?.Invoke(contextJson);
            }
            else
            {
                outputLine?.Invoke(line);
            }
        }
    }

    private static void ValidateId(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        if (taskId.Any(character =>
                (character < 'a' || character > 'z') &&
                !char.IsAsciiDigit(character) &&
                character != '-'))
        {
            throw new TaskValidationException("Task id must match [a-z0-9-]+.");
        }
    }
}
