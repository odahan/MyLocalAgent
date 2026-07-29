namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Announces a durably recorded incident to interested view models.
/// </summary>
/// <param name="CorrelationId">The user-visible correlation identifier.</param>
/// <param name="Message">The incident summary.</param>
public sealed record IncidentNotificationMessage(
    string CorrelationId,
    string Message);
