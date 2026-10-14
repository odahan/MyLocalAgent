namespace OD.MyLocalAgent.Core;

/// <summary>
/// Defines configuration keys that are persisted only in the secret store.
/// </summary>
public static class SecretKeys
{
    /// <summary>
    /// Gets the OpenAI API-key configuration key.
    /// </summary>
    public const string OpenAIApiKey = "OpenAI:ApiKey";

    /// <summary>
    /// Gets the Dropbox access-token configuration key.
    /// </summary>
    public const string DropboxAccessToken = "Dropbox:AccessToken";

    /// <summary>
    /// Gets the Dropbox refresh-token configuration key.
    /// </summary>
    public const string DropboxRefreshToken = "Dropbox:RefreshToken";

    /// <summary>
    /// Gets the Dropbox application-key configuration key.
    /// </summary>
    public const string DropboxAppKey = "Dropbox:AppKey";

    /// <summary>
    /// Gets the Web Search API-key configuration key.
    /// </summary>
    public const string WebSearchApiKey = "WebSearch:ApiKey";

    /// <summary>Gets the independent Tavily API-key configuration key.</summary>
    public const string TavilyApiKey = "WebSearch:TavilyApiKey";

    /// <summary>
    /// Gets all secret configuration keys loaded at bootstrap.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        OpenAIApiKey,
        DropboxAccessToken,
        DropboxRefreshToken,
        DropboxAppKey,
        WebSearchApiKey,
        TavilyApiKey,
    ];
}
