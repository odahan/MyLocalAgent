using CommunityToolkit.Mvvm.Messaging;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Messages;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Bridges core incident events to UI messages without coupling core services to WPF.
/// </summary>
internal sealed class IncidentMessengerBridge
{
    private readonly IIncidentReporter _incidentReporter;
    private readonly IMessenger _messenger;
    private bool _started;

    public IncidentMessengerBridge(
        IIncidentReporter incidentReporter,
        IMessenger messenger)
    {
        _incidentReporter = incidentReporter;
        _messenger = messenger;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _incidentReporter.IncidentReported += OnIncidentReported;
        _started = true;
    }

    private void OnIncidentReported(
        object? sender,
        IncidentReportedEventArgs e)
    {
        _messenger.Send(
            new IncidentNotificationMessage(
                e.Incident.CorrelationId,
                e.Incident.Message));
    }
}
