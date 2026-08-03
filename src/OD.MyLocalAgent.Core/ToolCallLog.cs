using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace OD.MyLocalAgent.Core;

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
            var summary = summarizeResult?.Invoke(result);
            if (string.IsNullOrWhiteSpace(summary))
            {
                logger.LogInformation(
                    "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome}.",
                    name,
                    timeProvider.GetElapsedTime(started).TotalMilliseconds,
                    "Success");
            }
            else
            {
                logger.LogInformation(
                    "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome}. {ToolResultSummary}",
                    name,
                    timeProvider.GetElapsedTime(started).TotalMilliseconds,
                    "Success",
                    summary);
            }
            return result;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome} and exception type {ExceptionType}.",
                name,
                timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Failure",
                exception.GetType().Name);
            throw;
        }
    }
}
