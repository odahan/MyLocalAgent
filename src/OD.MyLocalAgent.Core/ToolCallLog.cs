using Microsoft.Extensions.Logging;

namespace OD.MyLocalAgent.Core;

internal static class ToolCallLog
{
    public static async Task<string> ExecuteAsync(
        ILogger logger,
        TimeProvider timeProvider,
        string name,
        object sanitizedArguments,
        Func<Task<string>> operation)
    {
        var started = timeProvider.GetTimestamp();
        logger.LogInformation(
            "Tool {ToolName} started with arguments {@ToolArguments}.",
            name,
            sanitizedArguments);

        try
        {
            var result = await operation().ConfigureAwait(false);
            logger.LogInformation(
                "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome}.",
                name,
                timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Success");
            return result;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "Tool {ToolName} completed in {DurationMs} ms with outcome {Outcome}.",
                name,
                timeProvider.GetElapsedTime(started).TotalMilliseconds,
                "Failure");
            throw;
        }
    }
}
