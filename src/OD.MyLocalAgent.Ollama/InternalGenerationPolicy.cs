namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Defines one named, program-owned internal generation policy.
/// </summary>
/// <param name="Name">The stable policy name.</param>
/// <param name="Temperature">The fixed sampling temperature.</param>
/// <param name="TopP">The fixed nucleus-sampling probability mass.</param>
public sealed record InternalGenerationPolicy(
    string Name,
    double Temperature,
    double TopP);
