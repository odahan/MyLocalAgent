using System.Diagnostics;
using FluentAssertions;
using OD.MyLocalAgent.Ollama;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies OS-owned cross-process leases, embedding priority, cancellation, and cleanup ordering.</summary>
public sealed class DecisionResidencyTests : IDisposable
{
    private const string Endpoint = "http://127.0.0.1:11434";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "decision-lock-tests-" + Guid.NewGuid().ToString("N"));
    private DecisionModelResidencyCoordinator Coordinator() => new(_directory, TimeProvider.System);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Reader intent prevents end-of-operation unloading while another reader still owns progress.</summary>
    [Fact]
    public async Task TwoOwnersPreventPrematureCleanup()
    {
        var first = Coordinator();
        var second = Coordinator();
        using var owner1 = first.BeginOperation(Endpoint, "tev1:latest");
        var owner2 = second.BeginOperation(Endpoint, "tev1:4b");
        var unloads = 0;
        await first.CleanupAsync(Endpoint, "tev1:latest", () => { unloads++; return Task.CompletedTask; }, default);
        unloads.Should().Be(0);
        owner1.Dispose();
        await first.CleanupAsync(Endpoint, "tev1:latest", () => { unloads++; return Task.CompletedTask; }, default);
        unloads.Should().Be(0);
        owner2.Dispose();
        await second.CleanupAsync(Endpoint, "tev1:4b", () => { unloads++; return Task.CompletedTask; }, default);
        unloads.Should().Be(1);
    }

    /// <summary>Pending embeddings run at the next request boundary, while the reader retains its operation intent.</summary>
    [Fact]
    public async Task EmbeddingHandoffRunsBeforeNextReaderRequest()
    {
        var coordinator = Coordinator();
        using var owner = coordinator.BeginOperation(Endpoint, "tev1:latest");
        var firstEntered = Signal();
        var finishFirst = Signal();
        var embeddingEntered = Signal();
        var finishEmbedding = Signal();
        var first = coordinator.DecisionAsync(Endpoint, async () =>
        {
            firstEntered.SetResult(); await finishFirst.Task; return 1;
        }, default);
        await firstEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var embedding = Coordinator().EmbeddingAsync(Endpoint, async () =>
        {
            embeddingEntered.SetResult(); await finishEmbedding.Task; return 2;
        }, default);
        var secondEntered = false;
        var second = Coordinator().DecisionAsync(Endpoint, () => { secondEntered = true; return Task.FromResult(3); }, default);
        finishFirst.SetResult();
        await embeddingEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        secondEntered.Should().BeFalse();
        finishEmbedding.SetResult();
        (await first).Should().Be(1);
        (await embedding).Should().Be(2);
        (await second.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(3);
    }

    /// <summary>Cancellation while waiting consumes no model request and releases priority intent.</summary>
    [Fact]
    public async Task CanceledEmbeddingWaitReleasesIntent()
    {
        var coordinator = Coordinator();
        var entered = Signal();
        var release = Signal();
        var request = coordinator.DecisionAsync(Endpoint, async () => { entered.SetResult(); await release.Task; return 1; }, default);
        await entered.Task;
        using var canceled = new CancellationTokenSource();
        var embedding = coordinator.EmbeddingAsync(Endpoint, () => Task.FromResult(2), canceled.Token);
        canceled.Cancel();
        await FluentActions.Awaiting(() => embedding).Should().ThrowAsync<OperationCanceledException>();
        release.SetResult();
        await request;
        (await coordinator.DecisionAsync(Endpoint, () => Task.FromResult(3), default).WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(3);
    }

    /// <summary>Process death releases OS handle ownership; empty rendezvous files cannot keep a model locked.</summary>
    [Fact]
    public async Task ProcessDeathReleasesOperationOwnership()
    {
        var coordinator = Coordinator();
        using (coordinator.BeginOperation(Endpoint, "tev1:latest")) { }
        var path = Directory.GetFiles(_directory).Single().Replace("'", "''", StringComparison.Ordinal);
        var start = new ProcessStartInfo("powershell.exe") { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile");
        start.ArgumentList.Add("-NonInteractive");
        start.ArgumentList.Add("-Command");
        start.ArgumentList.Add("$lease = [System.IO.File]::Open('" + path + "', [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read); [Console]::WriteLine('ready'); Start-Sleep -Seconds 30");
        using var process = Process.Start(start)!;
        try
        {
            (await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10))).Should().Be("ready");
            var unloaded = false;
            await coordinator.CleanupAsync(Endpoint, "tev1:latest", () => { unloaded = true; return Task.CompletedTask; }, default);
            unloaded.Should().BeFalse();
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await coordinator.CleanupAsync(Endpoint, "tev1:latest", () => { unloaded = true; return Task.CompletedTask; }, default);
            unloaded.Should().BeTrue();
        }
        finally { if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); } }
    }

    /// <summary>Deletes isolated rendezvous files after all handles and processes are closed.</summary>
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
