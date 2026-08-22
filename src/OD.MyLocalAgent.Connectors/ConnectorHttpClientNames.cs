namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Provides named HTTP client keys so connector-specific policies remain isolated.
/// </summary>
internal static class ConnectorHttpClientNames
{
    public const string Google = "OD.MyLocalAgent.Google";

    public const string Dropbox = "OD.MyLocalAgent.Dropbox";

    public const string WebSearch = "OD.MyLocalAgent.WebSearch";
}
