using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the private Runner-to-UI workflow-context channel.
/// </summary>
public sealed class RunnerContextProtocolTests
{
    /// <summary>
    /// Verifies JSON context round-trips without appearing as ordinary runner text.
    /// </summary>
    [Fact]
    public void ContextRoundTripsThroughOneProtocolLine()
    {
        const string json = """{"steps":{"source":{"count":2}}}""";

        var line = RunnerContextProtocol.Encode(json);
        var decoded = RunnerContextProtocol.TryDecode(line, out var result);

        decoded.Should().BeTrue();
        result.Should().Be(json);
        line.Should().StartWith(RunnerContextProtocol.Prefix);
        line.Should().NotContain("source");
    }

    /// <summary>
    /// Verifies normal output and malformed protocol lines are not consumed as context.
    /// </summary>
    [Theory]
    [InlineData("Runner started.")]
    [InlineData("ODMLA_CONTEXT:not-base64")]
    [InlineData("ODMLA_CONTEXT:bm90LWpzb24=")]
    public void NonContextLinesAreRejected(string line)
    {
        RunnerContextProtocol.TryDecode(line, out _).Should().BeFalse();
    }
}
