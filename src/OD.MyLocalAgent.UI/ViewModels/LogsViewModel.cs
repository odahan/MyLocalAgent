using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;
using Serilog.Events;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Provides a dispatcher-safe filtered view of the in-memory Serilog buffer.
/// </summary>
public sealed partial class LogsViewModel : ObservableObject
{
    private readonly ObservableCollection<LogEntryItem> _entries = [];

    [ObservableProperty]
    private string _selectedMinimumLevel = nameof(LogEventLevel.Information);

    [ObservableProperty]
    private string _sourceFilter = string.Empty;

    [ObservableProperty]
    private string _taskIdFilter = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="LogsViewModel"/> class.
    /// </summary>
    public LogsViewModel(IInMemoryLogSink logSink)
    {
        MinimumLevels = Enum.GetNames<LogEventLevel>();
        Entries = CollectionViewSource.GetDefaultView(_entries);
        Entries.Filter = FilterEntry;
        foreach (var logEvent in logSink.GetSnapshot())
        {
            _entries.Add(CreateEntry(logEvent));
        }

        logSink.EventEmitted += OnEventEmitted;
    }

    /// <summary>
    /// Gets the available minimum-level filters.
    /// </summary>
    public IReadOnlyList<string> MinimumLevels { get; }

    /// <summary>
    /// Gets the filtered, read-only live event view.
    /// </summary>
    public ICollectionView Entries { get; }

    partial void OnSelectedMinimumLevelChanged(string value)
    {
        Entries.Refresh();
    }

    partial void OnSourceFilterChanged(string value)
    {
        Entries.Refresh();
    }

    partial void OnTaskIdFilterChanged(string value)
    {
        Entries.Refresh();
    }

    private bool FilterEntry(object candidate)
    {
        if (candidate is not LogEntryItem entry)
        {
            return false;
        }

        var minimumLevel = Enum.TryParse<LogEventLevel>(
            SelectedMinimumLevel,
            ignoreCase: true,
            out var parsed)
            ? parsed
            : LogEventLevel.Information;
        return entry.LevelValue >= (int)minimumLevel &&
               entry.Source.Contains(
                   SourceFilter ?? string.Empty,
                   StringComparison.OrdinalIgnoreCase) &&
               entry.TaskId.Contains(
                   TaskIdFilter ?? string.Empty,
                   StringComparison.OrdinalIgnoreCase);
    }

    private void OnEventEmitted(LogEvent logEvent)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            _entries.Add(CreateEntry(logEvent));
            return;
        }

        _ = dispatcher.BeginInvoke(
            () => _entries.Add(CreateEntry(logEvent)));
    }

    private static LogEntryItem CreateEntry(LogEvent logEvent)
    {
        var source = GetScalar(logEvent, "SourceContext");
        var taskId = GetScalar(logEvent, "TaskId");
        return new LogEntryItem(
            logEvent.Timestamp.ToLocalTime().ToString(
                "HH:mm:ss.fff",
                CultureInfo.InvariantCulture),
            logEvent.Level.ToString(),
            source,
            taskId,
            logEvent.RenderMessage(CultureInfo.CurrentCulture),
            logEvent.Exception?.ToString() ?? string.Empty,
            (int)logEvent.Level);
    }

    private static string GetScalar(LogEvent logEvent, string name) =>
        logEvent.Properties.TryGetValue(name, out var value) &&
        value is ScalarValue scalar
            ? Convert.ToString(scalar.Value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
}
