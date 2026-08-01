using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Represents one persisted runner execution in the Runs list.
/// </summary>
public sealed partial class RunListItem : ObservableObject
{
    [ObservableProperty]
    private string _status;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunListItem"/> class.
    /// </summary>
    /// <param name="info">The run descriptor.</param>
    public RunListItem(RunLogRunInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        FilePath = info.FilePath;
        TaskId = info.TaskId;
        StartedUtc = info.StartedUtc;
        Started = info.StartedUtc.ToLocalTime().ToString(
            "yyyy-MM-dd HH:mm:ss",
            CultureInfo.InvariantCulture);
        _status = FormatStatus(info);
    }

    /// <summary>
    /// Gets the absolute run-log file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the executed task id.
    /// </summary>
    public string TaskId { get; }

    /// <summary>
    /// Gets the UTC execution start.
    /// </summary>
    public DateTimeOffset StartedUtc { get; }

    /// <summary>
    /// Gets the local execution start display text.
    /// </summary>
    public string Started { get; }

    /// <summary>
    /// Updates the completion state from a fresh descriptor.
    /// </summary>
    /// <param name="info">The refreshed run descriptor.</param>
    public void Update(RunLogRunInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        Status = FormatStatus(info);
    }

    /// <summary>
    /// Marks the run as ended from tail-read end-record data.
    /// </summary>
    /// <param name="exitCode">The runner exit code.</param>
    public void MarkEnded(int exitCode)
    {
        Status = FormatStatus(exitCode);
    }

    private static string FormatStatus(RunLogRunInfo info) =>
        info.EndedUtc is null || info.ExitCode is null
            ? "In progress"
            : FormatStatus(info.ExitCode.Value);

    private static string FormatStatus(int exitCode) =>
        exitCode == 0
            ? "Success"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Failed ({exitCode})");
}
