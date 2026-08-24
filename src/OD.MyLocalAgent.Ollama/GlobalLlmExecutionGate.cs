using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Limits simultaneous model requests across all application and Runner processes.
/// </summary>
internal sealed class GlobalLlmExecutionGate
{
    private const string SemaphoreName = "Global\\OD.MyLocalAgent.LlmExecution";
    private readonly IOptionsMonitor<LlmExecutionOptions>? _options;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalLlmExecutionGate"/> class.
    /// </summary>
    /// <param name="options">The reloadable execution-limit options.</param>
    public GlobalLlmExecutionGate(IOptionsMonitor<LlmExecutionOptions>? options = null)
    {
        _options = options;
    }

    /// <summary>
    /// Waits for a global model-execution slot.
    /// </summary>
    /// <param name="cancellationToken">The token used to cancel the wait.</param>
    /// <returns>A lease which releases the slot when disposed.</returns>
    public async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        var maximumCount = _options?.CurrentValue.MaxConcurrentRequests ?? 1;
        var semaphore = CreateSemaphore(maximumCount);

        try
        {
            await Task.Run(
                () => WaitForSlot(semaphore, cancellationToken),
                CancellationToken.None).ConfigureAwait(false);
            return new Lease(semaphore);
        }
        catch
        {
            semaphore.Dispose();
            throw;
        }
    }

    private static void WaitForSlot(Semaphore semaphore, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!semaphore.WaitOne(TimeSpan.FromMilliseconds(100)))
            {
                continue;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                return;
            }

            _ = semaphore.Release();
            cancellationToken.ThrowIfCancellationRequested();
        }
    }

    private static Semaphore CreateSemaphore(int maximumCount)
    {
        return new Semaphore(
            initialCount: maximumCount,
            maximumCount: maximumCount,
            name: SemaphoreName);
    }

    private sealed class Lease(Semaphore semaphore) : IDisposable
    {
        private Semaphore? _semaphore = semaphore;

        public void Dispose()
        {
            var semaphore = Interlocked.Exchange(ref _semaphore, null);
            if (semaphore is null)
            {
                return;
            }

            _ = semaphore.Release();
            semaphore.Dispose();
        }
    }
}
