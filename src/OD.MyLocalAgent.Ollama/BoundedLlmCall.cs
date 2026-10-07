using System.Diagnostics;

namespace OD.MyLocalAgent.Ollama;

/// <summary>Provides a request-owned gate deadline and timing observer without shared mutable execution state.</summary>
public sealed class BoundedLlmCall(int gateWaitSeconds, Action<double> observeGateWait)
{
    /// <summary>The internal transport-only option key, removed before provider serialization.</summary>
    public const string OptionKey = "OD.DecisionCallBoundary";

    /// <summary>Waits for the ordinary cross-process gate with an independently bounded deadline.</summary>
    public async Task<IDisposable> EnterAsync(GlobalLlmExecutionGate gate, CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(gateWaitSeconds));
        try { return await gate.EnterAsync(timeout.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new LocalDecisionException("GateWaitTimeout", gateWaitMs: timer.Elapsed.TotalMilliseconds); }
        finally { observeGateWait(timer.Elapsed.TotalMilliseconds); }
    }
}
