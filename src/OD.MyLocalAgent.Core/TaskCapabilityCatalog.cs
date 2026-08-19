namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides stable user-facing metadata for sensitive task capabilities.
/// </summary>
public static class TaskCapabilityCatalog
{
    /// <summary>Gets all known capabilities in display order.</summary>
    public static IReadOnlyList<TaskCapabilityDefinition> Definitions { get; } =
    [
        new(TaskCapability.GmailSend, "Send Gmail messages", "Allows the task to send email through Gmail."),
        new(TaskCapability.PermanentFileDeletion, "Permanently delete files", "Allows irreversible deletion of authorized files and folders."),
        new(TaskCapability.RecycleBin, "Move files to Recycle Bin", "Allows authorized files and folders to be moved to the Windows Recycle Bin."),
        new(TaskCapability.HttpPost, "Send HTTP POST requests", "Allows outbound HTTP POST requests to configured hosts."),
        new(TaskCapability.SqliteWrite, "Write SQLite databases", "Allows modifying statements against authorized SQLite database files."),
        new(TaskCapability.PowerShellExecution, "Run PowerShell scripts", "Allows unrestricted local scripts. Managed workspace path protections do not apply inside PowerShell."),
        new(TaskCapability.ShellExecute, "Launch with Windows shell", "Allows unrestricted files, links, and applications to launch. Managed workspace path protections do not apply inside child processes."),
        new(TaskCapability.ArchiveWrite, "Write and extract archives", "Allows archive extraction and ZIP creation or updates in authorized locations."),
    ];

    /// <summary>Finds metadata for a known capability.</summary>
    public static TaskCapabilityDefinition Find(TaskCapability capability) =>
        Definitions.First(definition => definition.Capability == capability);
}

/// <summary>Describes one sensitive task capability.</summary>
public sealed record TaskCapabilityDefinition(
    TaskCapability Capability,
    string DisplayName,
    string Description);
