namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Selects the currently configured language-model provider.
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>
    /// Creates a view of the currently selected provider.
    /// </summary>
    /// <returns>The selected provider service.</returns>
    /// <exception cref="LlmConfigurationException">The active provider configuration is invalid.</exception>
    ILlmClientService Create();
}
