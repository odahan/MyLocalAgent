namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Describes a user-friendly scheduler preset and its five-field cron expression.
/// </summary>
/// <param name="Label">The plain-language preset label.</param>
/// <param name="Expression">The five-field cron expression.</param>
public sealed record CronSchedulePreset(string Label, string Expression);
