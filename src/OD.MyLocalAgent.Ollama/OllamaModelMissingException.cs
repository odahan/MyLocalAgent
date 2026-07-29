namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Represents a configured Ollama model that is not installed locally.
/// </summary>
public sealed class OllamaModelMissingException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OllamaModelMissingException"/> class.
    /// </summary>
    /// <param name="model">The missing model identifier.</param>
    public OllamaModelMissingException(string model)
        : base($"The Ollama model '{model}' is not installed. Pull the model or select an installed model.")
    {
        Model = model;
    }

    /// <summary>
    /// Gets the missing model identifier.
    /// </summary>
    public string Model { get; }
}
