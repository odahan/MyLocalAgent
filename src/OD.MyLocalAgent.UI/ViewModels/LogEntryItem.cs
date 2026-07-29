namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Presents one rendered Serilog event to the read-only logs page.
/// </summary>
/// <param name="Timestamp">The local timestamp.</param>
/// <param name="Level">The event level.</param>
/// <param name="Source">The source context.</param>
/// <param name="TaskId">The optional task identifier.</param>
/// <param name="Message">The rendered event message.</param>
/// <param name="Exception">The rendered exception, when present.</param>
/// <param name="LevelValue">The numeric event level used for filtering.</param>
public sealed record LogEntryItem(
    string Timestamp,
    string Level,
    string Source,
    string TaskId,
    string Message,
    string Exception,
    int LevelValue);
