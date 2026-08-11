using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using OD.MyLocalAgent.Connectors;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies strict, bounded, cache-aware compact Guardrails batching.
/// </summary>
public sealed class CompactContentTrustServiceTests
{
    /// <summary>
    /// Verifies one to twelve eligible items use exactly three tool-less provider calls.
    /// </summary>
    [Theory]
    [InlineData(1)]
    [InlineData(12)]
    public async Task FittingEligibleItemsUseThreeCalls(int itemCount)
    {
        var items = CreateItems(itemCount);
        var chat = new StubChatClient(
            [ScreenResponse(items), SummaryResponse(items), ScreenResponseForSummaries(items)]);
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        result.Items.Should().HaveCount(itemCount)
            .And.OnlyContain(item => item.Status == CompactExternalContentStatus.Summarized);
        result.Failures.Should().BeEmpty();
        chat.CallCount.Should().Be(3);
        chat.OptionCalls.Should().AllSatisfy(
            options =>
            {
                options.Should().NotBeNull();
                options!.Tools.Should().BeNullOrEmpty();
                options.Temperature.Should().Be(0);
                options.TopP.Should().Be(1);
            });
        chat.OptionCalls[1]!.MaxOutputTokens.Should().NotBeNull();
    }

    /// <summary>
    /// Verifies thirteen items partition stably into two three-call batches.
    /// </summary>
    [Fact]
    public async Task ThirteenItemsUseSixCallsAndPreserveOrder()
    {
        var items = CreateItems(13);
        var first = items.Take(12).ToArray();
        var second = items.Skip(12).ToArray();
        var chat = new StubChatClient(
            [
                ScreenResponse(first), SummaryResponse(first), ScreenResponseForSummaries(first),
                ScreenResponse(second), SummaryResponse(second), ScreenResponseForSummaries(second),
            ]);
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        chat.CallCount.Should().Be(6);
        result.Items.Select(item => item.Id).Should().Equal(items.Select(item => item.Id));
        chat.MessageCalls[0][1].Text.Should().Contain("item-12").And.NotContain("item-13");
        chat.MessageCalls[3][1].Text.Should().Contain("item-13");
    }

    /// <summary>
    /// Verifies low-trust sources never enter summarization.
    /// </summary>
    [Fact]
    public async Task LowTrustBatchUsesSourceScreeningOnly()
    {
        var items = CreateItems(5);
        var chat = new StubChatClient(ScreenResponse(items, score: 20));
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        chat.CallCount.Should().Be(1);
        result.Items.Should().OnlyContain(
            item => item.Status == CompactExternalContentStatus.SourceWithheld &&
                item.Summary == null);
    }

    /// <summary>
    /// Verifies an invalid first batch fails closed without preventing a later batch.
    /// </summary>
    [Fact]
    public async Task MalformedFirstBatchDoesNotAbortSecondBatch()
    {
        var items = CreateItems(13);
        var second = items.Skip(12).ToArray();
        var chat = new StubChatClient(
            ["not-json", ScreenResponse(second), SummaryResponse(second), ScreenResponseForSummaries(second)]);
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        chat.CallCount.Should().Be(4);
        result.Failures.Should().ContainSingle(failure => failure.Phase == "SourceScreening");
        result.Items.Take(12).Should().OnlyContain(
            item => item.Status == CompactExternalContentStatus.SourceScreeningUnavailable);
        result.Items[12].Status.Should().Be(CompactExternalContentStatus.Summarized);
        result.Failures[0].Exception.InnerException.Should().BeNull();
        result.Failures[0].Exception.Message.Should().NotContain("source-");
    }

    /// <summary>
    /// Verifies strict duplicate, unknown, and missing id handling rejects partial success.
    /// </summary>
    [Fact]
    public async Task InvalidIdSetFailsTheCompletePhase()
    {
        var items = CreateItems(2);
        var chat = new StubChatClient(
            """{"results":[{"id":"item-1","score":90,"indicators":[]},{"id":"item-1","score":90,"indicators":[]}]}""");
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        result.Failures.Should().ContainSingle();
        result.Items.Should().OnlyContain(
            item => item.Status == CompactExternalContentStatus.SourceScreeningUnavailable);
    }

    /// <summary>
    /// Verifies summary and final-screen failures never expose generated text.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LaterPhaseFailureReturnsNoGeneratedText(bool failSummarization)
    {
        var items = CreateItems(2);
        var responses = failSummarization
            ? new[] { ScreenResponse(items), "not-json" }
            : new[] { ScreenResponse(items), SummaryResponse(items), "not-json" };
        var chat = new StubChatClient(responses);
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        result.Failures.Should().ContainSingle();
        result.Items.Should().OnlyContain(item => item.Summary == null);
        result.Items.Should().OnlyContain(item => item.Status == (failSummarization
            ? CompactExternalContentStatus.SummarizationUnavailable
            : CompactExternalContentStatus.FinalScreeningUnavailable));
        chat.CallCount.Should().Be(failSummarization ? 2 : 3);
    }

    /// <summary>
    /// Verifies effective summary trust uses the per-item minimum and indicator union.
    /// </summary>
    [Fact]
    public async Task EffectiveTrustUsesPerItemMinimumAndIndicatorUnion()
    {
        var items = new[]
        {
            new CompactExternalContentItem("one", "source suspicious-one", 100),
            new CompactExternalContentItem("two", "source two", 100),
        };
        var chat = new StubChatClient(
            [
                """{"results":[{"id":"two","score":92,"indicators":[]},{"id":"one","score":70,"indicators":["suspicious-one"]}]}""",
                """{"results":[{"id":"one","summary":"summary final-one"},{"id":"two","summary":"summary two"}]}""",
                """{"results":[{"id":"one","score":60,"indicators":["final-one"]},{"id":"two","score":80,"indicators":[]}]}""",
            ]);
        var service = CreateService(chat, cacheResults: false);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        result.Items[0].SummaryTrust!.TrustScore.Should().Be(60);
        result.Items[0].SummaryTrust!.SuspiciousIndicators.Should()
            .Equal("suspicious-one", "final-one");
        result.Items[1].SummaryTrust!.TrustScore.Should().Be(80);
        result.Items[1].SummaryTrust!.SuspiciousIndicators.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies per-item caches ignore batch ids and composition and can eliminate all calls.
    /// </summary>
    [Fact]
    public async Task FullyCachedItemsUseZeroAdditionalCallsAcrossIdsAndOrder()
    {
        var first = CreateItems(2);
        var chat = new StubChatClient(
            [ScreenResponse(first), SummaryResponse(first), ScreenResponseForSummaries(first)]);
        var service = CreateService(chat, cacheResults: true);
        _ = await service.ProcessCompactAsync(first, CancellationToken.None);
        var reordered = new[]
        {
            first[1] with { Id = "changed-b" },
            first[0] with { Id = "changed-a" },
        };

        var second = await service.ProcessCompactAsync(reordered, CancellationToken.None);

        chat.CallCount.Should().Be(3);
        second.Items.Select(item => item.Id).Should().Equal("changed-b", "changed-a");
    }

    /// <summary>
    /// Verifies caller cancellation stops immediately without a fallback batch result.
    /// </summary>
    [Fact]
    public async Task CallerCancellationPropagatesImmediately()
    {
        var chat = new StubChatClient(ScreenResponse(CreateItems(1))) { WaitForCancellation = true };
        var service = CreateService(chat, cacheResults: false);
        using var source = new CancellationTokenSource();
        source.CancelAfter(TimeSpan.FromMilliseconds(20));

        var action = () => service.ProcessCompactAsync(CreateItems(13), source.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        chat.CallCount.Should().Be(1);
    }

    /// <summary>
    /// Exercises thirteen mixed Gmail fixtures with a low-trust item and a failed middle batch.
    /// </summary>
    [Fact]
    public async Task SyntheticThirteenMessageAcceptanceScenarioIsBoundedAndContinues()
    {
        var messages = Enumerable.Range(1, 13)
            .Select(index => CreateAcceptanceMessage(index))
            .ToArray();
        var options = new GuardrailsOptions
        {
            MaxContentChars = 2_048,
            MaxSummaryChars = 256,
            MaxSummarizationSourceChars = 10_000,
            CacheResults = false,
        };
        var projectedItems = messages.Select(
            (message, index) => new CompactExternalContentItem(
                $"item-{index + 1}",
                GmailContentProjector.Project(message, options.MaxContentChars).Content,
                256)).ToArray();
        projectedItems.Take(12).Should().OnlyContain(item => item.Content.Length == 2_048);
        var first = projectedItems.Take(6).ToArray();
        var firstEligible = first.Where(item => item.Id != "item-4").ToArray();
        var last = projectedItems.Skip(12).ToArray();
        var firstSource = JsonSerializer.Serialize(
            new
            {
                results = first.Select(item => new
                {
                    id = item.Id,
                    score = item.Id == "item-4" ? 20 : 95,
                    indicators = Array.Empty<string>(),
                }),
            });
        var chat = new StubChatClient(
            [
                firstSource,
                SummaryResponse(firstEligible),
                ScreenResponseForSummaries(firstEligible),
                "not-json",
                ScreenResponse(last),
                SummaryResponse(last),
                ScreenResponseForSummaries(last),
            ]);
        var service = CreateService(chat, options);
        var connector = new Mock<IGmailConnector>(MockBehavior.Strict);
        connector.Setup(instance => instance.GetUnreadMessagesAsync(
                It.IsAny<int?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);
        var reporter = new StubIncidentReporter();
        var tool = new GmailTool(
            connector.Object,
            new MutableOptionsMonitor<GuardrailsOptions>(options),
            service,
            reporter,
            NullLogger<GmailTool>.Instance,
            TimeProvider.System);

        var json = await tool.ReadUnreadAsync(cancellationToken: CancellationToken.None);

        chat.CallCount.Should().Be(7);
        reporter.Reports.Should().ContainSingle();
        chat.MessageCalls[1][1].Text.Should().NotContain("item-4");
        using var document = JsonDocument.Parse(json);
        var delivered = document.RootElement.GetProperty("messages");
        delivered.GetArrayLength().Should().Be(13);
        delivered[3].GetProperty("bodyProcessingStatus").GetString()
            .Should().Be("SourceWithheld");
        delivered[6].GetProperty("bodyProcessingStatus").GetString()
            .Should().Be("SourceScreeningUnavailable");
        delivered[12].GetProperty("contentWasSummarized").GetBoolean().Should().BeTrue();
        json.Should().NotContain("BODY-CANARY-");
        json.Should().NotContain("explicit instruction directed at an AI");
    }

    /// <summary>
    /// Verifies compact input validation occurs before provider resolution.
    /// </summary>
    [Fact]
    public async Task InvalidInputIsRejectedBeforeProviderResolution()
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        var service = CreateService(factory.Object, new GuardrailsOptions());
        var items = new[]
        {
            new CompactExternalContentItem("duplicate", "one", 100),
            new CompactExternalContentItem("duplicate", "two", 100),
        };

        var action = () => service.ProcessCompactAsync(items, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>();
        factory.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies a provider-model change invalidates the active compact batch.
    /// </summary>
    [Fact]
    public async Task ModelSnapshotChangeFailsClosed()
    {
        var items = CreateItems(2);
        var modelOptions = new MutableOptionsMonitor<OllamaOptions>(
            new OllamaOptions { Model = "model-a" });
        var chat = new StubChatClient(ScreenResponse(items))
        {
            ResponseFactory = _ =>
            {
                modelOptions.Set(new OllamaOptions { Model = "model-b" });
                return new ChatResponse(
                    new ChatMessage(ChatRole.Assistant, ScreenResponse(items)));
            },
        };
        var provider = new StubLlmClientService(chat, AIOptions.OllamaProvider);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var service = new ContentTrustService(
            factory.Object,
            new MutableOptionsMonitor<GuardrailsOptions>(
                new GuardrailsOptions { CacheResults = false }),
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            modelOptions,
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            TimeProvider.System);

        var result = await service.ProcessCompactAsync(items, CancellationToken.None);

        result.Failures.Should().ContainSingle(failure => failure.Phase == "SourceScreening");
        result.Items.Should().OnlyContain(
            item => item.Status == CompactExternalContentStatus.SourceScreeningUnavailable);
        chat.CallCount.Should().Be(1);
    }

    /// <summary>
    /// Verifies application configuration rejects projection budgets below 2048 characters.
    /// </summary>
    [Fact]
    public void GuardrailsConfigurationRequiresProjectionMinimum()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Guardrails:MaxContentChars"] = "2047",
                    ["Guardrails:MaxSummaryChars"] = "256",
                    ["Guardrails:MaxSummarizationSourceChars"] = "100000",
                })
            .Build();
        using var provider = new ServiceCollection()
            .AddGuardrails(configuration)
            .BuildServiceProvider();

        var action = () => provider.GetRequiredService<IOptions<GuardrailsOptions>>().Value;

        action.Should().Throw<OptionsValidationException>();
    }

    private static CompactExternalContentItem[] CreateItems(int count) =>
        Enumerable.Range(1, count)
            .Select(index => new CompactExternalContentItem(
                $"item-{index}",
                $"source-{index}",
                100))
            .ToArray();

    private static EmailMessageDto CreateAcceptanceMessage(int index)
    {
        var subject = index switch
        {
            2 => "Newsletter",
            3 => "Benign discussion of prompt injection",
            4 => "Explicit injection attempt",
            5 => new string('S', 1_400),
            _ => $"Ordinary message {index}",
        };
        var prefix = index switch
        {
            3 => "A benign security discussion mentions prompt injection defensively.",
            4 => "This contains an explicit instruction directed at an AI.",
            6 => "Demande en français avec une échéance demain.",
            7 => "Current reply",
            _ => $"Ordinary factual content {index}.",
        };
        var body = index switch
        {
            13 => string.Empty,
            7 => prefix + " BODY-CANARY-7 " + new string('g', 3_000) +
                "\nOn Monday, Alex wrote:\nquoted history",
            _ => prefix + " BODY-CANARY-" + index + " " +
                new string((char)('a' + index % 20), 3_000),
        };
        return new EmailMessageDto(
            $"provider-{index}",
            $"sender-{index}@example.com",
            "reader@example.com",
            subject,
            index == 2 ? "Monthly newsletter snippet" : $"Snippet {index}",
            body,
            DateTimeOffset.Parse("2026-08-03T08:00:00Z").AddMinutes(index),
            index % 2 == 0);
    }

    private static string ScreenResponse(
        IReadOnlyList<CompactExternalContentItem> items,
        int score = 95) =>
        JsonSerializer.Serialize(
            new
            {
                results = items.Select(item => new { id = item.Id, score, indicators = Array.Empty<string>() }),
            });

    private static string SummaryResponse(IReadOnlyList<CompactExternalContentItem> items) =>
        JsonSerializer.Serialize(
            new
            {
                results = items.Select(item => new { id = item.Id, summary = $"summary-{item.Id}" }),
            });

    private static string ScreenResponseForSummaries(
        IReadOnlyList<CompactExternalContentItem> items) =>
        ScreenResponse(items);

    private static ContentTrustService CreateService(
        StubChatClient chat,
        bool cacheResults)
        => CreateService(chat, new GuardrailsOptions { CacheResults = cacheResults });

    private static ContentTrustService CreateService(
        StubChatClient chat,
        GuardrailsOptions options)
    {
        var provider = new StubLlmClientService(chat, AIOptions.OllamaProvider);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        return new ContentTrustService(
            factory.Object,
            new MutableOptionsMonitor<GuardrailsOptions>(options),
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            new MutableOptionsMonitor<OllamaOptions>(
                new OllamaOptions { Model = "test-model" }),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            TimeProvider.System);
    }

    private static ContentTrustService CreateService(
        ILlmClientFactory factory,
        GuardrailsOptions options) =>
        new(
            factory,
            new MutableOptionsMonitor<GuardrailsOptions>(options),
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            new MutableOptionsMonitor<OllamaOptions>(
                new OllamaOptions { Model = "test-model" }),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            TimeProvider.System);
}
