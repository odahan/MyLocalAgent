using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the opt-in Runner diagnostic protocol.
/// </summary>
public sealed class RunnerDebugProtocolTests
{
    /// <summary>
    /// Verifies one diagnostic event round-trips as formatted JSON.
    /// </summary>
    [Fact]
    public void DiagnosticRoundTripsThroughOneProtocolLine()
    {
        var line = RunnerDebugProtocol.Encode(
            "llm.request",
            "guardrails",
            new { prompt = "diagnostic content" });

        var decoded = RunnerDebugProtocol.TryDecode(line, out var diagnosticJson);

        decoded.Should().BeTrue();
        diagnosticJson.Should().Contain("llm.request")
            .And.Contain("guardrails")
            .And.Contain("diagnostic content");
        line.Should().StartWith(RunnerDebugProtocol.Prefix)
            .And.NotContain("diagnostic content");
    }

    /// <summary>
    /// Verifies ordinary and malformed lines are not consumed as diagnostics.
    /// </summary>
    [Theory]
    [InlineData("Runner started.")]
    [InlineData("ODMLA_DEBUG:not-base64")]
    [InlineData("ODMLA_DEBUG:bnVsbA==")]
    public void InvalidDiagnosticLinesAreRejected(string line)
    {
        RunnerDebugProtocol.TryDecode(line, out _).Should().BeFalse();
    }
}
