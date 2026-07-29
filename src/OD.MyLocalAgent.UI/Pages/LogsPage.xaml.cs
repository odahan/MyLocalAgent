using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace OD.MyLocalAgent.UI.Pages;

/// <summary>
/// Displays the live, filtered, read-only log stream.
/// </summary>
public partial class LogsPage : UserControl
{
    private INotifyCollectionChanged? _items;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogsPage"/> class.
    /// </summary>
    public LogsPage()
    {
        InitializeComponent();
    }

    private void OnLogsListLoaded(object sender, RoutedEventArgs e)
    {
        _items = LogsList.Items;
        _items.CollectionChanged += OnItemsCollectionChanged;
        ScrollToLatest();
    }

    private void OnLogsListUnloaded(object sender, RoutedEventArgs e)
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
        if (LogsList.Items.Count > 0)
        {
            LogsList.ScrollIntoView(LogsList.Items[^1]);
        }
    }
}
