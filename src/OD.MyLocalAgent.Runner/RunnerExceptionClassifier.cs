using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Runner;

internal static class RunnerExceptionClassifier
{
    public static int GetExitCode(Exception exception) =>
        Contains<OptionsValidationException>(exception) ||
        Contains<LlmConfigurationException>(exception) ||
        Contains<ConnectorConfigurationException>(exception)
            ? RunnerExitCodes.ConfigurationError
            : RunnerExitCodes.TaskFailure;

    public static TException? Find<TException>(Exception exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException typed)
            {
                return typed;
            }

            if (current is AggregateException aggregate)
            {
                var nested = aggregate.InnerExceptions
                    .Select(Find<TException>)
                    .FirstOrDefault(static candidate => candidate is not null);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool Contains<TException>(Exception exception)
        where TException : Exception =>
        Find<TException>(exception) is not null;
}
