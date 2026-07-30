using FluentAssertions;
using Moq;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies the documented contracts of the built-in workflow skills.
/// </summary>
public sealed class BuiltInSkillTests
{
    /// <summary>
    /// Verifies action extraction requests explicit checklist data without invention.
    /// </summary>
    [Fact]
    public async Task ExtractActionItemsUsesDocumentedBatchContract()
    {
        var provider = new StubLlmClientService("- [ ] Reply");
        var factory = CreateFactory(provider);
        var skill = new ExtractActionItemsSkill(factory.Object);

        var result = await skill.ExecuteAsync(
            "Two untrusted messages",
            CancellationToken.None);

        result.Should().Be("- [ ] Reply");
        provider.Client.LastMessages.Should().ContainSingle();
        provider.Client.LastMessages[0].Text.Should()
            .Contain("Markdown checklist rows")
            .And.Contain("Not stated")
            .And.Contain("never infer or invent")
            .And.Contain("Two untrusted messages");
        factory.VerifyAll();
    }

    /// <summary>
    /// Verifies email classification requests one bounded category per input message.
    /// </summary>
    [Fact]
    public async Task ClassifyEmailUsesDocumentedBatchContract()
    {
        var provider = new StubLlmClientService("| Message | Category |");
        var factory = CreateFactory(provider);
        var skill = new ClassifyEmailSkill(factory.Object);

        var result = await skill.ExecuteAsync(
            "A batch of untrusted emails",
            CancellationToken.None);

        result.Should().Be("| Message | Category |");
        provider.Client.LastMessages.Should().ContainSingle();
        provider.Client.LastMessages[0].Text.Should()
            .Contain("one of Urgent, ReplySoon, Waiting, or FYI")
            .And.Contain("one row per email")
            .And.Contain("Message, Category, Reason, and Deadline")
            .And.Contain("A batch of untrusted emails");
        factory.VerifyAll();
    }

    private static Mock<ILlmClientFactory> CreateFactory(
        ILlmClientService provider)
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        return factory;
    }
}
