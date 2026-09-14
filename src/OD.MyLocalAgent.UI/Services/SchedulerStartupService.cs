using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Reconciles persisted schedules with Windows Task Scheduler when the desktop app starts.
/// </summary>
internal sealed class SchedulerStartupService : BackgroundService
{
    private readonly ITaskSchedulerService _scheduler;
    private readonly IIncidentReporter _incidentReporter;
    private readonly ILogger<SchedulerStartupService> _logger;
    private readonly IMessenger _messenger;

    public SchedulerStartupService(
        ITaskSchedulerService scheduler,
        IIncidentReporter incidentReporter,
        ILogger<SchedulerStartupService> logger,
        IMessenger messenger)
    {
        _scheduler = scheduler;
        _incidentReporter = incidentReporter;
        _logger = logger;
        _messenger = messenger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            var result = await _scheduler
                .ResyncAllAsync(stoppingToken)
                .ConfigureAwait(false);
            _logger.LogInformation(
                "Startup schedule resynchronization completed: {Registered} registered, {Unregistered} removed, {FailureCount} failed.",
                result.Registered,
                result.Unregistered,
                result.Failures.Count);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Startup schedule resynchronization failed.");
            await _incidentReporter
                .ReportAsync(
                    exception,
                    "Scheduler startup resynchronization",
                    CancellationToken.None)
                .ConfigureAwait(false);
            _messenger.Send(
                new SnackbarNotificationMessage(
                    "One or more tasks can't be loaded. Open Tasks for the name and reason."));
        }
    }
}
