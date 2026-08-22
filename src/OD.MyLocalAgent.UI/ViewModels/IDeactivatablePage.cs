namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Defines a page view model that releases transient state when navigation leaves the page.
/// </summary>
internal interface IDeactivatablePage
{
    void Deactivate();
}
