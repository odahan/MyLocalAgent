using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Requests navigation to a shell destination.
/// </summary>
/// <param name="Route">The requested route.</param>
public sealed record NavigateMessage(PageRoute Route);
