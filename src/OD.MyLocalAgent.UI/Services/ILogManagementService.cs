using Serilog.Events;

namespace OD.MyLocalAgent.UI.Services;

/// <summary>
/// Exports and resets the application log buffer and rolling files.
/// </summary>
public interface ILogManagementService
{
    /// <summary>
    /// Exports a stable log-event snapshot.
    /// </summary>
    Task ExportAsync(
        string destinationPath,
        IReadOnlyList<LogEvent> events,
        CancellationToken cancellationToken);

    /// <summary>
    /// Archives and truncates rolling files, then clears the live buffer.
    /// </summary>
    Task ResetAsync(CancellationToken cancellationToken);
}
