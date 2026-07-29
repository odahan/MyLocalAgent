namespace OD.MyLocalAgent.Core;

/// <summary>
/// Provides allowlisted HTTP operations to agents and workflows.
/// </summary>
public interface IHttpTool
{
    /// <summary>
    /// Performs an HTTP GET.
    /// </summary>
    Task<string> GetAsync(string url, CancellationToken cancellationToken);

    /// <summary>
    /// Performs an HTTP POST when the safety gate is enabled.
    /// </summary>
    Task<string> PostAsync(
        string url,
        string body,
        string contentType,
        CancellationToken cancellationToken);

    /// <summary>
    /// Performs an HTTP GET and extracts readable text from HTML.
    /// </summary>
    Task<string> GetTextAsync(string url, CancellationToken cancellationToken);
}
