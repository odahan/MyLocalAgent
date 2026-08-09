namespace OD.MyLocalAgent.Core;

/// <summary>
/// Controls the freshness window used for date-based filtering in discovery runs.
/// </summary>
public enum FreshnessWindow
{
    /// <summary>
    /// Sliding window: starts at two calendar days, expands to 4 then 7 days
    /// when unique eligible documents are below the minimum threshold.
    /// </summary>
    Sliding,

    /// <summary>
    /// No date filtering; limits are applied by maxResultsPerQuery and
    /// maxRetrievalsPerRun only.
    /// </summary>
    None,
}
