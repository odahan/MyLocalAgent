namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides the application-owned paths associated with one configured workspace.
/// </summary>
public sealed class WorkspacePaths
{
    /// <summary>
    /// Gets the name of the application-owned directory inside the workspace.
    /// </summary>
    public const string ApplicationDirectoryName = ".od-my-local-agent";

    /// <summary>
    /// Initializes paths rooted in an absolute workspace directory.
    /// </summary>
    /// <param name="workspaceDirectory">The configured workspace root.</param>
    public WorkspacePaths(string workspaceDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceDirectory);
        if (!Path.IsPathRooted(workspaceDirectory))
        {
            throw new ArgumentException(
                "The workspace directory must be an absolute path.",
                nameof(workspaceDirectory));
        }

        WorkspaceDirectory = Path.GetFullPath(workspaceDirectory);
        ApplicationDataDirectory = Path.Combine(
            WorkspaceDirectory,
            ApplicationDirectoryName);
        TasksDirectory = Path.Combine(ApplicationDataDirectory, "tasks");
        TaskBackupsDirectory = Path.Combine(TasksDirectory, "backups");
        ExamplesDirectory = Path.Combine(ApplicationDataDirectory, "examples");
        TaskExamplesDirectory = Path.Combine(ExamplesDirectory, "tasks");
        DataDirectory = Path.Combine(ApplicationDataDirectory, "data");
        DirectChatDatabasePath = Path.Combine(DataDirectory, "direct-chat.db");
        KnowledgeDirectory = Path.Combine(DataDirectory, "knowledge");
        KnowledgeDatabasePath = Path.Combine(KnowledgeDirectory, "knowledge.db");
        KnowledgeDocumentsDirectory = Path.Combine(KnowledgeDirectory, "documents");
        KnowledgeStagingDirectory = Path.Combine(KnowledgeDirectory, "staging");
        LogsDirectory = Path.Combine(ApplicationDataDirectory, "logs");
        ApplicationLogsDirectory = Path.Combine(LogsDirectory, "application");
        RunLogsDirectory = Path.Combine(LogsDirectory, "runs");
        IncidentsPath = Path.Combine(LogsDirectory, "incidents.jsonl");
        OutputsDirectory = Path.Combine(ApplicationDataDirectory, "outputs");
        ReportsDirectory = Path.Combine(OutputsDirectory, "reports");
        ReportIndexPath = Path.Combine(ReportsDirectory, "index.jsonl");
    }

    /// <summary>Gets the configured workspace root.</summary>
    public string WorkspaceDirectory { get; }

    /// <summary>Gets the application-owned workspace directory.</summary>
    public string ApplicationDataDirectory { get; }

    /// <summary>Gets the persisted task directory.</summary>
    public string TasksDirectory { get; }

    /// <summary>Gets the archived task-definition directory.</summary>
    public string TaskBackupsDirectory { get; }

    /// <summary>Gets the shipped and user-maintained examples directory.</summary>
    public string ExamplesDirectory { get; }

    /// <summary>Gets the task examples directory.</summary>
    public string TaskExamplesDirectory { get; }

    /// <summary>Gets the application data-file directory.</summary>
    public string DataDirectory { get; }

    /// <summary>Gets the Direct Chat SQLite database path.</summary>
    public string DirectChatDatabasePath { get; }

    /// <summary>Gets the private Knowledge library directory.</summary>
    public string KnowledgeDirectory { get; }
    /// <summary>Gets the dedicated Knowledge database path.</summary>
    public string KnowledgeDatabasePath { get; }
    /// <summary>Gets the canonical imported-original directory.</summary>
    public string KnowledgeDocumentsDirectory { get; }
    /// <summary>Gets the temporary Knowledge ingestion directory.</summary>
    public string KnowledgeStagingDirectory { get; }

    /// <summary>Gets the workspace log root.</summary>
    public string LogsDirectory { get; }

    /// <summary>Gets the rolling application-log directory.</summary>
    public string ApplicationLogsDirectory { get; }

    /// <summary>Gets the per-execution runner-log directory.</summary>
    public string RunLogsDirectory { get; }

    /// <summary>Gets the incident JSONL path.</summary>
    public string IncidentsPath { get; }

    /// <summary>Gets the application-managed output root.</summary>
    public string OutputsDirectory { get; }

    /// <summary>Gets the generated report directory.</summary>
    public string ReportsDirectory { get; }

    /// <summary>Gets the generated report index path.</summary>
    public string ReportIndexPath { get; }

    /// <summary>
    /// Creates every standard application-owned workspace directory.
    /// </summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(ApplicationDataDirectory);
        if (OperatingSystem.IsWindows())
        {
            File.SetAttributes(
                ApplicationDataDirectory,
                File.GetAttributes(ApplicationDataDirectory) | FileAttributes.Hidden);
        }

        Directory.CreateDirectory(TasksDirectory);
        Directory.CreateDirectory(TaskBackupsDirectory);
        Directory.CreateDirectory(TaskExamplesDirectory);
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(KnowledgeDirectory);
        Directory.CreateDirectory(KnowledgeDocumentsDirectory);
        Directory.CreateDirectory(KnowledgeStagingDirectory);
        Directory.CreateDirectory(ApplicationLogsDirectory);
        Directory.CreateDirectory(RunLogsDirectory);
        Directory.CreateDirectory(ReportsDirectory);
    }
}
