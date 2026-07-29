using System.CommandLine;
using FluentAssertions;
using OD.MyLocalAgent.Runner;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies runner parser-level validation without constructing application services.
/// </summary>
public sealed class RunnerCommandParsingTests
{
    /// <summary>
    /// Verifies run requires the task option and exits with a parser failure.
    /// </summary>
    [Fact]
    public async Task RunRequiresTaskOption()
    {
        var root = RunnerCommandFactory.CreateRootCommand();
        var parseResult = root.Parse(["run"]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await parseResult.InvokeAsync(
            new InvocationConfiguration
            {
                Output = output,
                Error = error,
            },
            CancellationToken.None);

        parseResult.Errors.Should().NotBeEmpty();
        exitCode.Should().Be(1);
        error.ToString().Should().Contain("--task");
    }

    /// <summary>
    /// Verifies an unknown direct tool alias exits at parser level.
    /// </summary>
    [Fact]
    public async Task TestToolRejectsUnknownAliasAtParserLevel()
    {
        var root = RunnerCommandFactory.CreateRootCommand();
        var parseResult = root.Parse(
            [
                "test-tool",
                "--alias",
                "unknown.alias",
                "--args",
                "{}",
            ]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        var exitCode = await parseResult.InvokeAsync(
            new InvocationConfiguration
            {
                Output = output,
                Error = error,
            },
            CancellationToken.None);

        parseResult.Errors.Should().ContainSingle();
        exitCode.Should().Be(1);
        error.ToString().Should().Contain("Unknown Tool Map alias 'unknown.alias'");
    }
}
