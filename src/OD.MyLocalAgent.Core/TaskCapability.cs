using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Identifies an explicit task-owned permission for a sensitive side effect.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<TaskCapability>))]
public enum TaskCapability
{
    /// <summary>No sensitive permission.</summary>
    None = 0,
    /// <summary>Send Gmail messages.</summary>
    GmailSend,
    /// <summary>Permanently delete files or directories.</summary>
    PermanentFileDeletion,
    /// <summary>Move files or directories to the Recycle Bin.</summary>
    RecycleBin,
    /// <summary>Send HTTP POST requests.</summary>
    HttpPost,
    /// <summary>Modify SQLite databases.</summary>
    SqliteWrite,
    /// <summary>Execute PowerShell scripts.</summary>
    PowerShellExecution,
    /// <summary>Launch targets through Windows Shell Execute.</summary>
    ShellExecute,
    /// <summary>Write or extract archives.</summary>
    ArchiveWrite,
}
