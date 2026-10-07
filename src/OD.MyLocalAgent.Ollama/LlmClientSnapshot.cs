using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Ollama;

/// <summary>Freezes provider identity and transport settings for an isolated execution.</summary>
public sealed record LlmClientSnapshot(string Provider, string Model, IChatClient ChatClient, bool IsLocal)
{
    /// <summary>Verifies local model residency before protected raw source can enter a request.</summary>
    public Func<CancellationToken, Task<bool>>? ValidateLocalSourceAsync { get; init; }
}

/// <summary>Exposes immutable options without configuration reloads during an execution.</summary>
internal sealed class FrozenOptionsMonitor<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue => value;
    public T Get(string? name) => value;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}
