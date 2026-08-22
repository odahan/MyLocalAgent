namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Defines a page view model that refreshes transient state when it becomes active.
/// </summary>
internal interface IActivatablePage
{
    void Activate();
}
