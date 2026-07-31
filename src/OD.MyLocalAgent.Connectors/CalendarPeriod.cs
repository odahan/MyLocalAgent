namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Defines the local calendar period returned by a calendar query.
/// </summary>
public enum CalendarPeriod
{
    /// <summary>The current local day.</summary>
    Day,

    /// <summary>The current local Monday-to-Monday week.</summary>
    Week,

    /// <summary>The current local calendar month.</summary>
    Month,

    /// <summary>The current local calendar year.</summary>
    Year,
}
