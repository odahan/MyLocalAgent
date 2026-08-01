using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using OD.MyLocalAgent.UI.ViewModels;

namespace OD.MyLocalAgent.UI.Pages;

/// <summary>
/// Displays persisted runner executions with live tailing.
/// </summary>
public partial class RunMonitorPage : UserControl
{
    private INotifyCollectionChanged? _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunMonitorPage"/> class.
    /// </summary>
    public RunMonitorPage()
    {
        InitializeComponent();
    }

    private void OnLinesListLoaded(object sender, RoutedEventArgs e)
    {
        _items = LinesList.Items;
        _items.CollectionChanged += OnItemsCollectionChanged;
        ScrollToLatest();
    }

    private void OnLinesListUnloaded(object sender, RoutedEventArgs e)
    {
        if (_items is not null)
        {
            _items.CollectionChanged -= OnItemsCollectionChanged;
            _items = null;
        }
    }

    private void OnItemsCollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        ScrollToLatest();
    }

    private void ScrollToLatest()
    {
        if (DataContext is RunMonitorViewModel { IsAutoScrollEnabled: true } &&
            LinesList.Items.Count > 0)
        {
            LinesList.ScrollIntoView(LinesList.Items[^1]);
        }
    }
}
