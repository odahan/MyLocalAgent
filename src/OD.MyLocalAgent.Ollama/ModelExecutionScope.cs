namespace OD.MyLocalAgent.Ollama;

/// <summary>Holds a temporary model selection for one logical task or workflow step.</summary>
public static class ModelExecutionScope
{
    private static readonly AsyncLocal<string?> CurrentModel = new();
    /// <summary>Gets the model selected for the current asynchronous execution flow.</summary>
    public static string? ModelName => CurrentModel.Value;
    /// <summary>Temporarily selects a model and restores the previous selection when disposed.</summary>
    public static IDisposable Push(string? modelName)
    {
        var previous = CurrentModel.Value;
        CurrentModel.Value = string.IsNullOrWhiteSpace(modelName) ? null : modelName.Trim();
        return new Scope(previous);
    }
    private sealed class Scope(string? previous) : IDisposable { public void Dispose() => CurrentModel.Value = previous; }
}
