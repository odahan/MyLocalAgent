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
        Tool("gmail.readToday", "gmail_read_today", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.ReadTodayAsync)),
        Tool("gmail.readUnread", "gmail_read_unread", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.ReadUnreadAsync)),
        Tool("gmail.send", "gmail_send", ToolGroups.Gmail, typeof(GmailTool), nameof(GmailTool.SendAsync), ["to", "subject", "body"], "Safety:AllowSendEmail"),
        Tool("calendar.today", "calendar_today", ToolGroups.Calendar, typeof(CalendarTool), nameof(CalendarTool.TodayAsync)),
        Tool("calendar.create", "calendar_create", ToolGroups.Calendar, typeof(CalendarTool), nameof(CalendarTool.CreateAsync), ["summary", "start", "end", "description"]),
        Tool("dropbox.upload", "dropbox_upload", ToolGroups.Dropbox, typeof(DropboxTool), nameof(DropboxTool.UploadAsync), ["localPath", "dropboxPath"]),
        Tool("dropbox.download", "dropbox_download", ToolGroups.Dropbox, typeof(DropboxTool), nameof(DropboxTool.DownloadAsync), ["dropboxPath", "localPath"]),
        Tool("dropbox.list", "dropbox_list", ToolGroups.Dropbox, typeof(DropboxTool), nameof(DropboxTool.ListAsync), ["folderPath"]),
        Tool("filesystem.list", "filesystem_list", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.ListAsync), ["path"]),
        Tool("filesystem.read", "filesystem_read", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.ReadAsync), ["path"]),
        Tool("filesystem.write", "filesystem_write", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.WriteAsync), ["path", "content"]),
        Tool("filesystem.move", "filesystem_move", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.MoveAsync), ["from", "to"]),
        Tool("filesystem.rename", "filesystem_rename", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.RenameAsync), ["path", "newName"]),
        Tool("filesystem.setExtension", "filesystem_set_extension", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.SetExtensionAsync), ["path", "extension"]),
        Tool("filesystem.delete", "filesystem_delete", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.DeleteAsync), ["path"], "Safety:AllowFileDelete"),
        Tool("filesystem.createDirectory", "filesystem_create_directory", ToolGroups.FileSystem, typeof(FileSystemTool), nameof(FileSystemTool.CreateDirectoryAsync), ["path"]),
        Tool("http.get", "http_get", ToolGroups.Http, typeof(HttpTool), nameof(HttpTool.GetAsync), ["url"]),
        Tool("http.post", "http_post", ToolGroups.Http, typeof(HttpTool), nameof(HttpTool.PostAsync), ["url", "body", "contentType"], "Safety:AllowHttpPost"),
        Tool("http.getText", "http_get_text", ToolGroups.Http, typeof(HttpTool), nameof(HttpTool.GetTextAsync), ["url"]),
        Tool(
            "web.search",
            "web_search",
            ToolGroups.Web,
            typeof(WebSearchTool),
            nameof(WebSearchTool.SearchAsync),
            ["query", "maxResults"],
            optionalArguments: ["maxResults"]),
        Ai("ai.summarize", typeof(SummarizeTextSkill), ["text"]),
        Ai("ai.extractActionItems", typeof(ExtractActionItemsSkill), ["text"]),
        Ai("ai.classifyEmail", typeof(ClassifyEmailSkill), ["text"]),
        Ai(
            PromptAlias,
            typeof(MyLocalAgentService),
            ["prompt", "includeContext"],
            optionalArguments: ["includeContext"]),
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
        IReadOnlyList<string>? arguments = null,
        string? safetyFlag = null,
        IReadOnlyList<string>? optionalArguments = null) =>
        new ToolDefinition(
            alias,
            agentName,
            group,
            type,
            method,
            arguments ?? [],
            safetyFlag,
            IsAiStep: false)
        {
            OptionalArguments = optionalArguments ?? [],
        };

    private static ToolDefinition Ai(
        string alias,
        Type type,
        IReadOnlyList<string> arguments,
        IReadOnlyList<string>? optionalArguments = null) =>
        new(alias, null, "ai", type, null, arguments, null, IsAiStep: true)
        {
            OptionalArguments = optionalArguments ?? [],
        };
}
