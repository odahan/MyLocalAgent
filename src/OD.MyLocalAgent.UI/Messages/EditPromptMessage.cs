using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.UI.Messages;

/// <summary>
/// Requests the prompt editor for an existing task.
/// </summary>
/// <param name="TaskId">The task identifier.</param>
/// <param name="GenerationOptions">
/// The optional unsaved task-generation override snapshot.
/// </param>
/// <param name="UseGenerationOptionsSnapshot">
/// Whether the supplied override, including a null inherit-all snapshot, must be used for
/// prompt testing instead of the persisted task value.
/// </param>
public sealed record EditPromptMessage(
    string TaskId,
    GenerationOptions? GenerationOptions = null,
    bool UseGenerationOptionsSnapshot = false);
