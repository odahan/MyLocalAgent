using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Workflows;
using Serilog;

namespace OD.MyLocalAgent.Runner;

internal static class RunnerApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        IHost? host = null;
        var hostStarted = false;
        var rootCommand = RunnerCommandFactory.CreateRootCommand(
            () => host?.Services ??
                throw new InvalidOperationException(
                    "The runner host has not been started."),
            output,
            error);
        var parseResult = rootCommand.Parse(args);
        var invocationConfiguration = new InvocationConfiguration
        {
            Output = output,
            Error = error,
            EnableDefaultExceptionHandler = false,
        };

        if (parseResult.Errors.Count > 0)
        {
            return await parseResult.InvokeAsync(
                invocationConfiguration,
                cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var builder = Host.CreateApplicationBuilder(
                new HostApplicationBuilderSettings
                {
                    Args = [],
                    ContentRootPath = AppContext.BaseDirectory,
                });
            builder.AddODServices(
                static loggerConfiguration =>
                    loggerConfiguration.WriteTo.Console());
            if (IsInteractiveDebugEnabled())
            {
                builder.Services.Replace(
                    ServiceDescriptor.Singleton<IExecutionDebugTrace>(
                        new RunnerExecutionDebugTrace(output)));
            }
            builder.Services.AddWorkflowServices();
            host = builder.Build();

            var reporter = host.Services.GetRequiredService<IIncidentReporter>();
            AppExceptionHandlers.Initialize(reporter);
            AppExceptionHandlers.WireProcessEvents();
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
            hostStarted = true;

            return await parseResult.InvokeAsync(
                invocationConfiguration,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine("Runner operation cancelled.");
            return RunnerExitCodes.TaskFailure;
        }
        catch (Exception exception)
        {
            string? correlationId = null;
            try
            {
                correlationId = await ReportTopLevelExceptionAsync(
                    exception).ConfigureAwait(false);
            }
            catch (Exception reportingException)
            {
                error.WriteLine(
                    $"Incident reporting also failed: {reportingException.Message}");
            }

            var incidentText = correlationId is null
                ? string.Empty
                : $" Incident: {correlationId}.";
            error.WriteLine($"Runner failed: {exception.Message}.{incidentText}");
            return RunnerExceptionClassifier.GetExitCode(exception);
        }
        finally
        {
            if (host is not null)
            {
                if (hostStarted)
                {
                    try
                    {
                        await host.StopAsync(
                            CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        error.WriteLine(
                            $"Runner host shutdown warning: {exception.Message}");
                    }
                }

                host.Dispose();
            }
        }
    }

    private static async Task<string> ReportTopLevelExceptionAsync(
        Exception exception)
    {
        try
        {
            return await AppExceptionHandlers.HandleAsync(
                exception,
                "OD.MyLocalAgent.Runner",
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            var fallbackReporter = new IncidentReporter(
                new JsonlIncidentLogService(TimeProvider.System),
                TimeProvider.System);
            AppExceptionHandlers.Initialize(fallbackReporter);
            return await AppExceptionHandlers.HandleAsync(
                exception,
                "OD.MyLocalAgent.Runner",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool IsInteractiveDebugEnabled() =>
        string.Equals(
            Environment.GetEnvironmentVariable("ODMLA_RUNNER_DEBUG"),
            "1",
            StringComparison.Ordinal);
}
