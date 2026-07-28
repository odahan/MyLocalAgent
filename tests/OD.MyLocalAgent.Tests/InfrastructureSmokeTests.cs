using FluentAssertions;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies that the Stage 0 test infrastructure is operational.
/// </summary>
public sealed class InfrastructureSmokeTests
{
    /// <summary>
    /// Confirms that xUnit discovers and executes tests with FluentAssertions.
    /// </summary>
    [Fact]
    public void TestInfrastructureIsOperational()
    {
        true.Should().BeTrue();
    }
}
