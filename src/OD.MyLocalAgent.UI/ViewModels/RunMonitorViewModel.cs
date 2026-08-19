using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.UI.Services;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Monitors persisted runner executions: past runs, live tailing, export (§19.4).
/// </summary>
public sealed partial class RunMonitorViewModel :
    ObservableObject,
    IActivatablePage,
    IDeactivatablePage
{
    private const int ListRefreshTicks = 5;

    private readonly IRunLogCatalog _catalog;
    private readonly IDesktopInteractionService _desktopInteraction;
    private readonly WorkspacePaths _workspacePaths;
    private DispatcherTimer? _timer;
    private long _tailOffset;
    private int _tickCount;

    [ObservableProperty]
    private RunListItem? _selectedRun;

    [ObservableProperty]
    private bool _isAutoScrollEnabled = true;

    [ObservableProperty]
    private int _selectedLineLimit = 2_000;

    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunMonitorViewModel"/> class.
    /// </summary>
    public RunMonitorViewModel(
        IRunLogCatalog catalog,
        IDesktopInteractionService desktopInteraction,
        WorkspacePaths workspacePaths)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(desktopInteraction);
        ArgumentNullException.ThrowIfNull(workspacePaths);
        _catalog = catalog;
        _desktopInteraction = desktopInteraction;
        _workspacePaths = workspacePaths;
    }

    /// <summary>
    /// Gets the persisted runs, newest first.
    /// </summary>
    public ObservableCollection<RunListItem> Runs { get; } = [];

    /// <summary>
    /// Gets the rendered lines of the selected run, bounded by the line limit.
    /// </summary>
    public ObservableCollection<string> Lines { get; } = [];

    /// <summary>
    /// Gets the selectable display line limits.
    /// </summary>
    public IReadOnlyList<int> LineLimits { get; } = [500, 1_000, 2_000, 5_000, 10_000];

    /// <inheritdoc />
    public void Activate()
    {
        RefreshRuns();
        _timer ??= new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => PollOnce(),
            Dispatcher.CurrentDispatcher);
        _timer.Start();
    }

    /// <inheritdoc />
    public void Deactivate()
    {
        _timer?.Stop();
    }

    /// <summary>
    /// Executes one polling cycle: tails the selected run and periodically
    /// reconciles the run list.
    /// </summary>
    public void PollOnce()
    {
        _tickCount++;
        if (_tickCount >= ListRefreshTicks)
        {
            _tickCount = 0;
            RefreshRuns();
        }

        TailSelectedRun();
    }

    /// <summary>
    /// Reconciles the run list with the on-disk catalog, preserving the selection.
    /// </summary>
    [RelayCommand]
    public void RefreshRuns()
    {
        IReadOnlyList<RunLogRunInfo> infos;
        try
        {
            infos = _catalog.GetRuns();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Run list refresh failed: {exception.Message}";
            return;
        }

        var existing = Runs.ToDictionary(
            static run => run.FilePath,
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < infos.Count; index++)
        {
            var info = infos[index];
            if (existing.TryGetValue(info.FilePath, out var item))
            {
                item.Update(info);
                var currentIndex = Runs.IndexOf(item);
                if (currentIndex != index)
                {
                    Runs.Move(currentIndex, index);
                }
            }
            else
            {
                Runs.Insert(index, new RunListItem(info));
            }
        }

        for (var index = Runs.Count - 1; index >= infos.Count; index--)
        {
            var removed = Runs[index];
            Runs.RemoveAt(index);
            if (ReferenceEquals(removed, SelectedRun))
            {
                SelectedRun = null;
            }
        }
    }

    /// <summary>
    /// Clears only the rendered lines; the underlying file is not modified.
    /// </summary>
    [RelayCommand]
    public void ClearDisplay()
    {
        Lines.Clear();
    }

    /// <summary>
    /// Opens the run-log root folder in the Windows shell.
    /// </summary>
    [RelayCommand]
    private void OpenRunLogsFolder()
    {
        Directory.CreateDirectory(_workspacePaths.RunLogsDirectory);
        _desktopInteraction.OpenFolder(_workspacePaths.RunLogsDirectory);
    }

    /// <summary>
    /// Exports the complete selected run as plain text.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (SelectedRun is null)
        {
            StatusText = "Select a run to export.";
            return;
        }

        var suggestedName =
            $"{SelectedRun.TaskId}-" +
            SelectedRun.StartedUtc.ToLocalTime().ToString(
                "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture);
        var destination = _desktopInteraction.SelectSavePath(
            "Export run log",
            "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            ".txt",
            suggestedName);
        if (destination is null)
        {
            return;
        }

        try
        {
            var result = _catalog.ReadLines(SelectedRun.FilePath, 0);
            await File.WriteAllLinesAsync(
                destination,
                result.Lines.Select(Format)).ConfigureAwait(true);
            StatusText = $"Exported {result.Lines.Count} line(s) to {destination}.";
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Export failed: {exception.Message}";
        }
    }

    partial void OnSelectedRunChanged(RunListItem? value)
    {
        _tailOffset = 0;
        Lines.Clear();
        if (value is not null)
        {
            TailSelectedRun();
        }
    }

    partial void OnSelectedLineLimitChanged(int value)
    {
        TrimToLimit();
    }

    private void TailSelectedRun()
    {
        if (SelectedRun is null)
        {
            return;
        }

        RunLogReadResult result;
        try
        {
            result = _catalog.ReadLines(SelectedRun.FilePath, _tailOffset);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            StatusText = $"Run log read failed: {exception.Message}";
            return;
        }

        _tailOffset = result.NewOffset;
        foreach (var line in result.Lines)
        {
            Lines.Add(Format(line));
        }

        TrimToLimit();
        if (result.ExitCode is not null)
        {
            SelectedRun.MarkEnded(result.ExitCode.Value);
        }
    }

    private void TrimToLimit()
    {
        while (Lines.Count > SelectedLineLimit)
        {
            Lines.RemoveAt(0);
        }
    }

    private static string Format(RunLogLine line) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{line.TimestampUtc.ToLocalTime():HH:mm:ss.fff} " +
            $"[{(line.Stream == RunLogStream.Error ? "err" : "out")}] {line.Text}");
}
