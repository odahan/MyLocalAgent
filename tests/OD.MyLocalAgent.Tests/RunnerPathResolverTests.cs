using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies stable runner path resolution for published and configured layouts.
/// </summary>
public sealed class RunnerPathResolverTests
{
    /// <summary>
    /// Verifies an empty setting resolves the runner beside the current executable.
    /// </summary>
    [Fact]
    public void EmptyPathResolvesPublishedSibling()
    {
        var options = new SchedulerOptions();

        var path = RunnerPathResolver.Resolve(options, requireExists: false);

        path.Should().Be(
            Path.Combine(
                AppContext.BaseDirectory,
                RunnerPathResolver.RunnerFileName));
    }

    /// <summary>
    /// Verifies an explicit relative setting remains rooted beside the application.
    /// </summary>
    [Fact]
    public void RelativePathResolvesFromApplicationDirectory()
    {
        var options = new SchedulerOptions
        {
            RunnerPath = Path.Combine("runner", RunnerPathResolver.RunnerFileName),
        };

        var path = RunnerPathResolver.Resolve(options, requireExists: false);

        path.Should().Be(
            Path.GetFullPath(
                options.RunnerPath,
                AppContext.BaseDirectory));
    }
}
