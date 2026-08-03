using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines the single normative mapping shared by agents and workflows.
/// </summary>
public static class ToolMap
{
    /// <summary>
    /// Gets the prompt-step alias.
    /// </summary>
    public const string PromptAlias = "ai.prompt";

    private static readonly IReadOnlyList<ToolDefinition> Entries =
    [
        Tool("gmail.readToday", "gmail_read_today", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.ReadTodayAsync),
            "Reads Gmail messages received today.",
            [MaxMessages()]),
        Tool("gmail.readUnread", "gmail_read_unread", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.ReadUnreadAsync),
            "Reads unread Gmail messages.",
            [MaxMessages()]),
        Tool("gmail.readRecent", "gmail_read_recent", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.ReadRecentAsync),
            "Reads Gmail messages since the start of the local day a number of days ago.",
            [
                Arg("daysBack", WorkflowArgumentKind.Integer, required: true, "Number of local calendar days before today to include; zero means today only.", "3"),
                MaxMessages(),
            ]),
        Tool("gmail.send", "gmail_send", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.SendAsync),
            "Sends a plain-text Gmail message when Safety:AllowSendEmail is enabled.",
            [
                Arg("to", WorkflowArgumentKind.String, required: true, "Recipient email address.", "someone@example.com"),
                Arg("subject", WorkflowArgumentKind.String, required: true, "Message subject.", "Daily report"),
                Arg("body", WorkflowArgumentKind.String, required: true, "Plain-text message body.", "Here is today's summary."),
            ],
            "Safety:AllowSendEmail"),
        Tool("calendar.today", "calendar_today", ToolGroups.Calendar, typeof(CalendarTool), nameof(CalendarTool.TodayAsync),
            "Reads today's Google Calendar events."),
        Tool("calendar.read", "calendar_read", ToolGroups.Calendar, typeof(CalendarTool), nameof(CalendarTool.ReadAsync),
            "Reads Google Calendar events for the current local day, week, month, or year.",
            [
                Arg("period", WorkflowArgumentKind.Enumeration, required: true, "Calendar period: Day, Week, Month, or Year.", "Week",
                    allowedValues: Enum.GetNames<CalendarPeriod>()),
            ]),
        Tool("calendar.create", "calendar_create", ToolGroups.Calendar, typeof(CalendarTool), nameof(CalendarTool.CreateAsync),
            "Creates a Google Calendar event.",
            [
                Arg("summary", WorkflowArgumentKind.String, required: true, "Event summary.", "Team sync"),
                Arg("start", WorkflowArgumentKind.String, required: true, "Event start time with offset.", "2026-01-15T09:00:00+01:00"),
                Arg("end", WorkflowArgumentKind.String, required: true, "Event end time with offset.", "2026-01-15T10:00:00+01:00"),
                Arg("description", WorkflowArgumentKind.String, required: true, "Event description.", "Weekly team synchronisation."),
            ]),
        Tool("dropbox.upload", "dropbox_upload", ToolGroups.Dropbox, typeof(DropboxTool), nameof(DropboxTool.UploadAsync),
            "Uploads an authorized local file to Dropbox.",
            [
                Arg("localPath", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute local source path.", "reports/summary.txt"),
                Arg("dropboxPath", WorkflowArgumentKind.String, required: true, "Dropbox destination path.", "/Reports/summary.txt"),
            ],
            localPaths: [Path("localPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]),
        Tool("dropbox.download", "dropbox_download", ToolGroups.Dropbox, typeof(DropboxTool), nameof(DropboxTool.DownloadAsync),
            "Downloads a Dropbox file into an authorized local path.",
            [
                Arg("dropboxPath", WorkflowArgumentKind.String, required: true, "Dropbox source path.", "/Reports/summary.txt"),
                Arg("localPath", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute local destination path.", "reports/summary.txt"),
            ],
            localPaths: [Path("localPath", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]),
        Tool("dropbox.list", "dropbox_list", ToolGroups.Dropbox, typeof(DropboxTool), nameof(DropboxTool.ListAsync),
            "Lists the immediate children of a Dropbox folder.",
            [
                Arg("folderPath", WorkflowArgumentKind.String, required: true, "Dropbox folder path.", "/Reports"),
            ]),
        Tool("filesystem.list", "filesystem_list", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.ListAsync),
            "Lists immediate files and directories beneath an authorized local path.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root path or task-authorized absolute directory path.", "inbox"),
            ],
            localPaths: [Path("path", LocalPathTargetKind.Directory, FileSystemAccessMode.ReadOnly)]),
        Tool("filesystem.read", "filesystem_read", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.ReadAsync),
            "Reads a UTF-8 text file beneath an authorized local path.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root path or task-authorized absolute file path.", "inbox/source.txt"),
            ],
            localPaths: [Path("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadOnly)]),
        Tool("filesystem.write", "filesystem_write", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.WriteAsync),
            "Writes UTF-8 text to an authorized local file path.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root path or task-authorized absolute file path.", "reports/summary.txt"),
                Arg("content", WorkflowArgumentKind.String, required: true, "Text content to write.", "Report body."),
            ],
            localPaths: [Path("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite)]),
        Tool("filesystem.move", "filesystem_move", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.MoveAsync),
            "Moves a file or directory between authorized local paths.",
            [
                Arg("from", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute source path.", "inbox/source.txt"),
                Arg("to", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute destination path.", "archive/source.txt"),
            ],
            localPaths:
            [
                Path("from", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite),
                Path("to", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite),
            ]),
        Tool("filesystem.rename", "filesystem_rename", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.RenameAsync),
            "Renames an authorized local file without changing its directory.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute source file path.", "inbox/source.txt"),
                Arg("newName", WorkflowArgumentKind.String, required: true, "New file name only, without a directory path.", "renamed.txt"),
            ],
            localPaths:
            [
                Path("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite),
                new LocalPathArgumentDefinition("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite, LocalPathDerivationKind.SameDirectoryLeafName, "newName"),
            ]),
        Tool("filesystem.setExtension", "filesystem_set_extension", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.SetExtensionAsync),
            "Changes the extension of an authorized local file.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute source file path.", "inbox/source.txt"),
                Arg("extension", WorkflowArgumentKind.String, required: true, "New extension with or without a leading period, for example '.md' or 'md'.", ".md"),
            ],
            localPaths:
            [
                Path("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite),
                new LocalPathArgumentDefinition("path", LocalPathTargetKind.File, FileSystemAccessMode.ReadWrite, LocalPathDerivationKind.SameDirectoryExtension, "extension"),
            ]),
        Tool("filesystem.delete", "filesystem_delete", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.DeleteAsync),
            "Deletes a sandboxed file or directory when Safety:AllowFileDelete is enabled.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute file or directory path.", "inbox/obsolete.txt"),
            ],
            "Safety:AllowFileDelete",
            localPaths: [Path("path", LocalPathTargetKind.FileOrDirectory, FileSystemAccessMode.ReadWrite)]),
        Tool("filesystem.createDirectory", "filesystem_create_directory", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.CreateDirectoryAsync),
            "Creates a directory beneath an authorized local path.",
            [
                Arg("path", WorkflowArgumentKind.String, required: true, "Relative global-root or task-authorized absolute directory path.", "reports/2026"),
            ],
            localPaths: [Path("path", LocalPathTargetKind.Directory, FileSystemAccessMode.ReadWrite)]),
        Tool("http.get", "http_get", ToolGroups.Http, typeof(HttpTool), nameof(HttpTool.GetAsync),
            "Performs an HTTP GET request to an allowed host.",
            [Url()]),
        Tool("http.post", "http_post", ToolGroups.Http, typeof(HttpTool), nameof(HttpTool.PostAsync),
            "Performs an HTTP POST request when Safety:AllowHttpPost is enabled.",
            [
                Url(),
                Arg("body", WorkflowArgumentKind.String, required: true, "Request body.", "{ \"key\": \"value\" }"),
                Arg("contentType", WorkflowArgumentKind.String, required: true, "Request media type, such as application/json.", "application/json"),
            ],
            "Safety:AllowHttpPost"),
        Tool("http.getText", "http_get_text", ToolGroups.Http, typeof(HttpTool), nameof(HttpTool.GetTextAsync),
            "Downloads an allowed web page and returns a protected factual summary.",
            [Url()]),
        Tool(
            "web.search",
            "web_search",
            ToolGroups.Web,
            typeof(WebSearchTool),
            nameof(WebSearchTool.SearchAsync),
            "Searches the web through the configured cloud provider and returns one Guardrails-protected compact result set.",
            [
                Arg("query", WorkflowArgumentKind.String, required: true, "Search query sent to the configured cloud provider.", "local weather forecast"),
                Arg("maxResults", WorkflowArgumentKind.Integer, required: false, "Optional result limit; defaults to WebSearch:DefaultMaxResults.", "5"),
            ]),
        Ai("ai.summarize", typeof(SummarizeTextSkill),
            "Summarizes the supplied text, or the previous step output when text is omitted.",
            [AiText()]),
        Ai("ai.extractActionItems", typeof(ExtractActionItemsSkill),
            "Extracts action items from the supplied text, or from the previous step output when text is omitted.",
            [AiText()]),
        Ai("ai.classifyEmail", typeof(ClassifyEmailSkill),
            "Classifies the supplied email text, or the previous step output when text is omitted.",
            [AiText()]),
        Ai(
            PromptAlias,
            typeof(MyLocalAgentService),
            "Sends a workflow-authored prompt to the local model and returns its response.",
            [
                Arg("prompt", WorkflowArgumentKind.String, required: true, "Prompt text sent to the local model; placeholders are substituted before the call.", "Summarize the workflow context in five bullet points."),
                Arg("includeContext", WorkflowArgumentKind.Boolean, required: false, "Whether the complete accumulated workflow context is appended to the prompt; omitted means false.", "true"),
            ]),
    ];

    /// <summary>
    /// Gets all normative Tool Map entries.
    /// </summary>
    public static IReadOnlyList<ToolDefinition> Definitions => Entries;

    /// <summary>
    /// Resolves an alias case-insensitively.
    /// </summary>
    /// <param name="alias">The workflow alias.</param>
    /// <returns>The matching definition, or <see langword="null"/>.</returns>
    public static ToolDefinition? Find(string alias)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alias);
        return Entries.FirstOrDefault(
            entry => string.Equals(entry.Alias, alias, StringComparison.OrdinalIgnoreCase));
    }

    private static ToolDefinition Tool(
        string alias,
        string agentName,
        string group,
        Type type,
        string method,
        string description,
        IReadOnlyList<ToolArgumentDefinition>? arguments = null,
        string? safetyFlag = null,
        IReadOnlyList<LocalPathArgumentDefinition>? localPaths = null) =>
        new ToolDefinition(
            alias,
            agentName,
            group,
            type,
            method,
            Names(arguments),
            safetyFlag,
            IsAiStep: false)
        {
            Description = description,
            ArgumentDefinitions = arguments ?? [],
            OptionalArguments = OptionalNames(arguments),
            LocalPathArguments = localPaths ?? [],
        };

    private static ToolDefinition Ai(
        string alias,
        Type type,
        string description,
        IReadOnlyList<ToolArgumentDefinition> arguments) =>
        new(alias, null, ToolGroups.Ai, type, null, Names(arguments), null, IsAiStep: true)
        {
            Description = description,
            ArgumentDefinitions = arguments,
            OptionalArguments = OptionalNames(arguments),
        };

    private static IReadOnlyList<string> Names(IReadOnlyList<ToolArgumentDefinition>? arguments) =>
        arguments is null ? [] : [.. arguments.Select(static argument => argument.Name)];

    private static IReadOnlyList<string> OptionalNames(IReadOnlyList<ToolArgumentDefinition>? arguments) =>
        arguments is null
            ? []
            : [.. arguments.Where(static argument => !argument.IsRequired).Select(static argument => argument.Name)];

    private static ToolArgumentDefinition Arg(
        string name,
        WorkflowArgumentKind kind,
        bool required,
        string description,
        string? example = null,
        IReadOnlyList<string>? allowedValues = null) =>
        new(name, kind, required, description)
        {
            Example = example,
            AllowedValues = allowedValues ?? [],
            SupportsPlaceholders = kind is WorkflowArgumentKind.String or WorkflowArgumentKind.Enumeration,
        };

    private static ToolArgumentDefinition MaxMessages() =>
        Arg("maxMessages", WorkflowArgumentKind.Integer, required: false, "Optional maximum number of messages to return.", "20");

    private static ToolArgumentDefinition Url() =>
        Arg("url", WorkflowArgumentKind.String, required: true, "Absolute HTTP or HTTPS URL.", "https://example.com/api");

    private static ToolArgumentDefinition AiText() =>
        Arg("text", WorkflowArgumentKind.String, required: false, "Optional text to process; omit to use the previous step output.", "{{previous.output}}");

    private static LocalPathArgumentDefinition Path(
        string argumentName,
        LocalPathTargetKind targetKind,
        FileSystemAccessMode requiredAccess) =>
        new(argumentName, targetKind, requiredAccess);
}
