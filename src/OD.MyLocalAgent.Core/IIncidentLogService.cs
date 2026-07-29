namespace OD.MyLocalAgent.Core;

/// <summary>
/// Persists and manages application incidents in JSON Lines format.
/// </summary>
public interface IIncidentLogService
{
    /// <summary>
    /// Appends an incident.
    /// </summary>
    /// <param name="incident">The incident to append.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task AppendAsync(IncidentRecord incident, CancellationToken cancellationToken);

    /// <summary>
    /// Reads all valid incidents in file order.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The persisted incidents.</returns>
    Task<IReadOnlyList<IncidentRecord>> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Exports the current incident log.
    /// </summary>
    /// <param name="destinationPath">The destination JSONL path.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ExportAsync(string destinationPath, CancellationToken cancellationToken);

    /// <summary>
    /// Clears all persisted incidents.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task ClearAsync(CancellationToken cancellationToken);
}
