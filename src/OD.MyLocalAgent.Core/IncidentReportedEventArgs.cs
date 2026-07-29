namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides notification data for a reported incident.
/// </summary>
public sealed class IncidentReportedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IncidentReportedEventArgs"/> class.
    /// </summary>
    /// <param name="incident">The reported incident.</param>
    public IncidentReportedEventArgs(IncidentRecord incident)
    {
        ArgumentNullException.ThrowIfNull(incident);
        Incident = incident;
    }

    /// <summary>
    /// Gets the reported incident.
    /// </summary>
    public IncidentRecord Incident { get; }
}
