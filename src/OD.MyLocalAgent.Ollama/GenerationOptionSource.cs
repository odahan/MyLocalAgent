namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Identifies the source of one effective generation value.
/// </summary>
public enum GenerationOptionSource
{
    /// <summary>
    /// The value comes from global Settings.
    /// </summary>
    Settings,

    /// <summary>
    /// The value comes from the task override.
    /// </summary>
    Task,

    /// <summary>
    /// The value comes from the workflow-step override.
    /// </summary>
    Step,

    /// <summary>
    /// The value is owned by a fixed internal program policy.
    /// </summary>
    Program,
}
