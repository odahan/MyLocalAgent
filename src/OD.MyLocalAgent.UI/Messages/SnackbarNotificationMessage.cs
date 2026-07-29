namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Requests a non-blocking shell notification.
/// </summary>
/// <param name="Message">The notification text.</param>
public sealed record SnackbarNotificationMessage(string Message);
