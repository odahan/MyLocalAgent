using System.Security.Cryptography;
using System.Text;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Coordinates reader intent, request boundaries, and embedding priority across UI and Runner processes.
/// Open operating-system handles own leases; leftover empty rendezvous files never imply ownership.
/// Lock order is embedding intent, endpoint request, then global LLM gate. No gate owner waits for another lease.
/// </summary>
public sealed class DecisionModelResidencyCoordinator
{
    private readonly string _directory;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a coordinator using the application user's shared rendezvous directory.</summary>
    public DecisionModelResidencyCoordinator(TimeProvider timeProvider)
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OD.MyLocalAgent", "decision-leases"), timeProvider) { }

    internal DecisionModelResidencyCoordinator(string directory, TimeProvider timeProvider)
    {
        _directory = directory;
        _timeProvider = timeProvider;
    }

    /// <summary>Retains operation intent without preventing an embedding handoff at a request boundary.</summary>
    public IDisposable BeginOperation(string endpoint, string model)
    {
        var path = FileName(endpoint, "owners");
        EnsureFile(path);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    }

    /// <summary>Acquires operation intent with bounded cancellation when another process is finishing cleanup.</summary>
    public async Task<IDisposable> BeginOperationAsync(string endpoint, string model, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return BeginOperation(endpoint, model); }
            catch (IOException)
            { await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken).ConfigureAwait(false); }
        }
    }

    /// <summary>Serializes an active decision request and yields to pending embeddings between mails.</summary>
    public async Task<T> DecisionAsync<T>(string endpoint, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        while (true)
        {
            await WaitForEmbeddingAsync(endpoint, cancellationToken).ConfigureAwait(false);
            using var request = await AcquireAsync(FileName(endpoint, "request"), cancellationToken).ConfigureAwait(false);
            if (IsEmbeddingPending(endpoint)) continue;
            return await action().ConfigureAwait(false);
        }
    }

    /// <summary>Requests priority before waiting for active inference, then holds the boundary through embedding work.</summary>
    public async Task<T> EmbeddingAsync<T>(string endpoint, Func<Task<T>> action, CancellationToken cancellationToken)
    {
        using var intent = await AcquireAsync(FileName(endpoint, "embedding"), cancellationToken).ConfigureAwait(false);
        using var request = await AcquireAsync(FileName(endpoint, "request"), cancellationToken).ConfigureAwait(false);
        return await action().ConfigureAwait(false);
    }

    /// <summary>Unloads after the last operation owner releases intent, outside the global gate.</summary>
    public async Task CleanupAsync(string endpoint, string model, Func<Task> unload, CancellationToken cancellationToken)
    {
        using var request = await AcquireAsync(FileName(endpoint, "request"), cancellationToken).ConfigureAwait(false);
        FileStream owners;
        try { owners = OpenExclusive(FileName(endpoint, "owners")); }
        catch (IOException) { return; }
        using (owners) await unload().ConfigureAwait(false);
    }

    private async Task WaitForEmbeddingAsync(string endpoint, CancellationToken cancellationToken)
    {
        while (IsEmbeddingPending(endpoint))
            await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken).ConfigureAwait(false);
    }

    private bool IsEmbeddingPending(string endpoint)
    {
        try { using var lease = OpenExclusive(FileName(endpoint, "embedding")); return false; }
        catch (IOException) { return true; }
    }

    private async Task<FileStream> AcquireAsync(string path, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { return OpenExclusive(path); }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25), _timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private string FileName(string endpoint, string role)
    {
        Directory.CreateDirectory(_directory);
        var normalized = LocalDecisionEndpoint.IsValid(endpoint) ? LocalDecisionEndpoint.Identity(endpoint) : new Uri(endpoint).AbsoluteUri;
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized + "|" + role)));
        return Path.Combine(_directory, key + ".lease");
    }

    private static FileStream OpenExclusive(string path) => new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    private static void EnsureFile(string path)
    {
        if (File.Exists(path)) return;
        try { using var created = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.ReadWrite); }
        catch (IOException) when (File.Exists(path)) { }
    }
}
