namespace OD.MyLocalAgent.Core;

/// <summary>
/// Controls the freshness window used for date-based filtering in discovery runs.
/// </summary>
public enum FreshnessWindow
{
    /// <summary>
    /// Sliding window: collects once for seven days, then evaluates the 2, 4,
    /// and 7-day calendar windows locally until the minimum is reached.
    /// </summary>
    Sliding,

    /// <summary>
    /// No date filtering; the run-level search ceiling and compatibility
    /// safeguards still apply.
    /// </summary>
    None,
}
