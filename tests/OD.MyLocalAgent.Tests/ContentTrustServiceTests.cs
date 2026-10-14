using System.Text;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Moq;
using OD.MyLocalAgent.Guardrails;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;
using OllamaSharp.Models;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies tool-less trust screening, fail-closed delivery, caching, and reduction.
/// </summary>
public sealed class ContentTrustServiceTests
{
    /// <summary>
    /// Verifies a clean verdict uses a mandatory plaintext envelope and no tools.
    /// </summary>
    [Fact]
    public async Task CleanVerdictUsesToollessPlaintextEnvelope()
    {
        var chatClient = new StubChatClient(
            """{"score":91,"indicators":["suspicious span","invented"]}""");
        var service = CreateService(chatClient);
        const string content = "A benign note with suspicious span quoted for review.";

        var result = await service.ScreenAsync(
            content,
            returnBase64: false,
            CancellationToken.None);

        result.Status.Should().Be(ContentTrustStatus.Screened);
        result.TrustScore.Should().Be(91);
        result.ContentEncoding.Should().Be("none");
        result.Content.Should().Contain("NON-NEGOTIABLE SECURITY RULE");
        result.Content.Should().Contain(content);
        result.SuspiciousIndicators.Should().Equal("suspicious span");
        chatClient.CallCount.Should().Be(1);
        chatClient.LastMessages.Should().HaveCount(2);
        chatClient.LastMessages[0].Role.Should().Be(ChatRole.System);
        chatClient.LastMessages[1].Text.Should().Contain(content);
        chatClient.LastOptions.Should().NotBeNull();
        chatClient.LastOptions!.Temperature.Should().Be(0);
        chatClient.LastOptions.Tools.Should().BeNullOrEmpty();
        chatClient.LastOptions.MaxOutputTokens.Should().BeInRange(1024, 8192);
        var responseFormat = chatClient.LastOptions.ResponseFormat.Should().BeOfType<ChatResponseFormatJson>().Which;
        responseFormat.Schema.Should().NotBeNull();
        chatClient.LastOptions.AdditionalProperties.Should()
            .ContainKey(OllamaOption.Think.Name)
            .WhoseValue.Should().Be(false);
    }

    /// <summary>
    /// Verifies marked Base64 preserves the exact UTF-8 round trip.
    /// </summary>
    [Fact]
    public async Task RequestedBase64PreservesExactUnicodeContent()
    {
        var service = CreateService(
            new StubChatClient("""{"score":100,"indicators":[]}"""));
        const string content = "Line one\r\nÉté ☕\nLine three";

        var result = await service.ScreenAsync(
            content,
            returnBase64: true,
            CancellationToken.None);

        result.ContentEncoding.Should().Be("base64");
        DecodeEnvelope(result.Content).Should().Be(content);
    }

    /// <summary>
    /// Verifies policy boundaries and fail-closed non-screened statuses.
    /// </summary>
    [Theory]
    [InlineData(ContentTrustStatus.Screened, 100, TrustDisposition.PassThrough)]
    [InlineData(ContentTrustStatus.Screened, 80, TrustDisposition.PassThrough)]
    [InlineData(ContentTrustStatus.Screened, 79, TrustDisposition.EncodeBase64)]
    [InlineData(ContentTrustStatus.Screened, 50, TrustDisposition.EncodeBase64)]
    [InlineData(ContentTrustStatus.Screened, 49, TrustDisposition.Withhold)]
    [InlineData(ContentTrustStatus.Screened, 0, TrustDisposition.Withhold)]
    [InlineData(ContentTrustStatus.ScreeningDisabled, null, TrustDisposition.EncodeBase64)]
    [InlineData(ContentTrustStatus.Failed, null, TrustDisposition.EncodeBase64)]
    public void PolicyUsesNormativeThresholdBoundaries(
        ContentTrustStatus status,
        int? score,
        TrustDisposition expected)
    {
        var result = new ContentTrustResult(
            score,
            string.Empty,
            "none",
            status,
            [],
            "Stub",
            0);

        ContentTrustPolicy.Decide(result, new GuardrailsOptions())
            .Should().Be(expected);
    }

    /// <summary>
    /// Verifies disabled screening makes no provider call and returns marked Base64.
    /// </summary>
    [Fact]
    public async Task DisabledScreeningDoesNotResolveProvider()
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        var service = CreateService(
            factory.Object,
            new GuardrailsOptions { ScreeningEnabled = false });

        var result = await service.ScreenAsync(
            "external",
            returnBase64: false,
            CancellationToken.None);

        result.Status.Should().Be(ContentTrustStatus.ScreeningDisabled);
        result.TrustScore.Should().BeNull();
        result.ProviderName.Should().Be("Disabled");
        DecodeEnvelope(result.Content).Should().Be("external");
        factory.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies malformed verdicts throw a content-free typed failure with a safe fallback.
    /// </summary>
    [Fact]
    public async Task MalformedVerdictThrowsSafeTypedFallback()
    {
        const string content = "private external source";
        var service = CreateService(new StubChatClient("not-json"));

        var action = () => service.ScreenAsync(
            content,
            returnBase64: false,
            CancellationToken.None);

        var exception = await action.Should()
            .ThrowAsync<GuardrailScreeningException>();
        exception.Which.Message.Should().NotContain(content);
        exception.Which.FallbackResult.Status.Should().Be(ContentTrustStatus.Failed);
        exception.Which.FallbackResult.TrustScore.Should().BeNull();
        DecodeEnvelope(exception.Which.FallbackResult.Content).Should().Be(content);
    }

    /// <summary>
    /// Verifies sequential identical screening calls reuse one cached verdict.
    /// </summary>
    [Fact]
    public async Task IdenticalScreeningCallsUseVerdictCache()
    {
        var chatClient = new StubChatClient(
            """{"score":88,"indicators":[]}""");
        var service = CreateService(chatClient);

        _ = await service.ScreenAsync("same", false, CancellationToken.None);
        var second = await service.ScreenAsync("same", true, CancellationToken.None);

        chatClient.CallCount.Should().Be(1);
        second.LatencyMs.Should().Be(0);
        second.ContentEncoding.Should().Be("base64");
    }

    /// <summary>
    /// Verifies complete-source chunks and intermediate summaries are screened in order.
    /// </summary>
    [Fact]
    public async Task SummarizationCoversAllChunksAndAggregatesMinimumScore()
    {
        var chatClient = new StubChatClient(
            [
                """{"score":90,"indicators":[]}""",
                """{"summary":"A"}""",
                """{"score":40,"indicators":[]}""",
                """{"summary":"B"}""",
                """{"score":80,"indicators":[]}""",
                """{"summary":"AB"}""",
            ]);
        var options = new GuardrailsOptions
        {
            MaxContentChars = 5,
            MaxSummaryChars = 5,
            MaxSummarizationSourceChars = 100,
        };
        var service = CreateService(chatClient, options);

        var result = await service.SummarizeAsync(
            "abcdefgh",
            5,
            CancellationToken.None);

        result.Status.Should().Be(ContentSummaryStatus.Summarized);
        result.Summary.Should().Be("AB");
        result.SourceTrustScore.Should().Be(40);
        chatClient.CallCount.Should().Be(6);
        chatClient.MessageCalls[0][1].Text.Should().Contain("abcde");
        chatClient.MessageCalls[2][1].Text.Should().Contain("fgh");
        chatClient.OptionCalls.Should().OnlyContain(
            optionsCall =>
                optionsCall != null &&
                optionsCall.Temperature == 0 &&
                (optionsCall.Tools == null || optionsCall.Tools.Count == 0));
    }

    /// <summary>
    /// Verifies C# summary capping includes a marker and stays within the requested maximum.
    /// </summary>
    [Fact]
    public async Task LongSummaryIsCappedWithTruncationMarker()
    {
        var generated = new string('x', 300);
        var chatClient = new StubChatClient(
            [
                """{"score":95,"indicators":[]}""",
                $$"""{"summary":"{{generated}}"}""",
            ]);
        var options = new GuardrailsOptions
        {
            MaxContentChars = 1_000,
            MaxSummaryChars = 100,
            MaxSummarizationSourceChars = 2_000,
        };
        var service = CreateService(chatClient, options);

        var result = await service.SummarizeAsync(
            "source",
            100,
            CancellationToken.None);

        result.Status.Should().Be(ContentSummaryStatus.SummaryTruncated);
        result.Summary.Should().HaveLength(100);
        result.Summary.Should().Contain("AI summary truncated by");
    }

    /// <summary>
    /// Verifies sources above the safe limit make no provider or summarizer call.
    /// </summary>
    [Fact]
    public async Task SourceAboveLimitMakesNoProviderCall()
    {
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        var options = new GuardrailsOptions
        {
            MaxContentChars = 10,
            MaxSummaryChars = 10,
            MaxSummarizationSourceChars = 20,
        };
        var service = CreateService(factory.Object, options);

        var result = await service.SummarizeAsync(
            new string('x', 21),
            10,
            CancellationToken.None);

        result.Status.Should().Be(ContentSummaryStatus.SourceTooLarge);
        result.Summary.Should().BeEmpty();
        result.SourceTrustScore.Should().BeNull();
        factory.VerifyNoOtherCalls();
    }

    /// <summary>
    /// Verifies caller cancellation is propagated instead of becoming a screening failure.
    /// </summary>
    [Fact]
    public async Task CallerCancellationIsNotWrapped()
    {
        var service = CreateService(
            new StubChatClient("""{"score":100,"indicators":[]}"""));
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();

        var action = () => service.ScreenAsync(
            "external",
            returnBase64: false,
            cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    /// <summary>
    /// Verifies the independent guardrail timeout becomes a safe typed failure.
    /// </summary>
    [Fact]
    public async Task GuardrailTimeoutProducesSafeTypedFailure()
    {
        var chatClient = new StubChatClient(
            """{"score":100,"indicators":[]}""")
        {
            WaitForCancellation = true,
        };
        var service = CreateService(
            chatClient,
            new GuardrailsOptions { TimeoutSeconds = 1 });

        var action = () => service.ScreenAsync(
            "external",
            returnBase64: false,
            CancellationToken.None);

        var exception = await action.Should()
            .ThrowAsync<GuardrailScreeningException>();
        exception.Which.InnerException.Should().BeOfType<TimeoutException>();
        exception.Which.FallbackResult.Status.Should().Be(ContentTrustStatus.Failed);
        DecodeEnvelope(exception.Which.FallbackResult.Content).Should().Be("external");
    }

    /// <summary>
    /// Verifies Guardrails ignores user sampling defaults and retains the global context declaration.
    /// </summary>
    [Fact]
    public async Task ScreeningUsesFixedPolicyAndGlobalContext()
    {
        var chat = new StubChatClient(
            """{"score":100,"indicators":[]}""");
        var provider = new StubLlmClientService(
            chat,
            AIOptions.OllamaProvider);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        var service = new ContentTrustService(
            factory.Object,
            new MutableOptionsMonitor<GuardrailsOptions>(
                new GuardrailsOptions()),
            new MutableOptionsMonitor<AIOptions>(
                new AIOptions
                {
                    GenerationDefaults = new AIGenerationDefaults
                    {
                        Temperature = 1.9,
                        TopP = 0.1,
                        ContextWindowTokens = 22_000,
                    },
                }),
            new MutableOptionsMonitor<OllamaOptions>(
                new OllamaOptions { Model = "test-model" }),
            new MutableOptionsMonitor<OpenAIOptions>(
                new OpenAIOptions()),
            TimeProvider.System);

        _ = await service.ScreenAsync(
            "external",
            returnBase64: false,
            CancellationToken.None);

        chat.LastOptions.Should().NotBeNull();
        chat.LastOptions!.Temperature.Should().Be(0);
        chat.LastOptions.TopP.Should().Be(1);
        chat.LastOptions.AdditionalProperties.Should()
            .ContainKey(OllamaOption.NumCtx.Name)
            .WhoseValue.Should().Be(22_000);
    }

    private static ContentTrustService CreateService(
        StubChatClient chatClient,
        GuardrailsOptions? guardrailsOptions = null)
    {
        var provider = new StubLlmClientService(
            chatClient,
            AIOptions.OllamaProvider);
        var factory = new Mock<ILlmClientFactory>(MockBehavior.Strict);
        factory.Setup(instance => instance.Create()).Returns(provider);
        return CreateService(factory.Object, guardrailsOptions ?? new GuardrailsOptions());
    }

    private static ContentTrustService CreateService(
        ILlmClientFactory factory,
        GuardrailsOptions guardrailsOptions) =>
        new(
            factory,
            new MutableOptionsMonitor<GuardrailsOptions>(guardrailsOptions),
            new MutableOptionsMonitor<AIOptions>(new AIOptions()),
            new MutableOptionsMonitor<OllamaOptions>(
                new OllamaOptions { Model = "test-model" }),
            new MutableOptionsMonitor<OpenAIOptions>(new OpenAIOptions()),
            TimeProvider.System);

    private static string DecodeEnvelope(string envelope)
    {
        var lines = envelope.Split(Environment.NewLine);
        var delimiterIndex = Array.FindIndex(
            lines,
            static line =>
                line.StartsWith(
                    "<<<UNTRUSTED-EXTERNAL-DATA-BASE64 ",
                    StringComparison.Ordinal));
        delimiterIndex.Should().BeGreaterThanOrEqualTo(0);
        return Encoding.UTF8.GetString(
            Convert.FromBase64String(lines[delimiterIndex + 1]));
    }
}
