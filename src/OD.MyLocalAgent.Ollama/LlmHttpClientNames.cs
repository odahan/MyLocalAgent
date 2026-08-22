namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Provides named HTTP client keys so each LLM provider can have independent configuration.
/// </summary>
internal static class LlmHttpClientNames
{
    public const string Ollama = "OD.MyLocalAgent.Ollama";

    public const string OpenAI = "OD.MyLocalAgent.OpenAI";
}
