using System.Text.Json;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Wraps tool execution with consistently sanitized logging and timing collection.
/// </summary>
internal static class ToolCallLog
{
    public static async Task<string> ExecuteAsync(
        ILogger logger,
        TimeProvider timeProvider,
        string name,
        object sanitizedArguments,
        Func<Task<string>> operation,
        Func<string, string?>? summarizeResult = null)
    {
        var started = timeProvider.GetTimestamp();
        var argumentsJson = JsonSerializer.Serialize(
            sanitizedArguments,
            ToolJson.Options);
        logger.LogInformation(
            "Tool {ToolName} started with arguments {ToolArgumentsJson}.",
            name,
            argumentsJson);

        try
        {
            var result = await operation().ConfigureAwait(false);
            var elapsed = timeProvider.GetElapsedTime(started);
            TaskTimingCollector.Current.Value?.Record(TaskTimingCollector.ToolCategory, name, elapsed);
            var summary = summarizeResult?.Invoke(result);
            if (string.IsNullOrWhiteSpace(summary))
            {
                logger.LogInformation(
                    "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome}.",
                    name,
                    elapsed.TotalMilliseconds,
                    "Success");
            }
            else
            {
                logger.LogInformation(
                    "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome}. {ToolResultSummary}",
                    name,
                    elapsed.TotalMilliseconds,
                    "Success",
                    summary);
            }
            return result;
        }
        catch (Exception exception)
        {
            var elapsed = timeProvider.GetElapsedTime(started);
            TaskTimingCollector.Current.Value?.Record(TaskTimingCollector.ToolCategory, name, elapsed);
            logger.LogWarning(
                "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome} and exception type {ExceptionType}.",
                name,
                elapsed.TotalMilliseconds,
                "Failure",
                exception.GetType().Name);
            throw;
        }
    }
}
