using System.Net;
using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.Tests.TestDoubles;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Exercises the isolated local HTTP contract with recording transports, never Ollama or Gmail.</summary>
public sealed class LocalDecisionClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "decision-http-tests-" + Guid.NewGuid().ToString("N"));
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();
    private static LocalDecisionRequest Request() => new("http://127.0.0.1:11434", "tev1:latest", "Synthetic source only",
        Json("{\"q\":{\"type\":\"noul\",\"instructions\":\"Is this a greeting?\"}}"), 15, 30, 10);
    private OllamaLocalDecisionClient Client(StubHttpMessageHandler handler) => new(new StubHttpClientFactory(handler),
        new MutableOptionsMonitor<LlmExecutionOptions>(new()), new DecisionModelResidencyCoordinator(_directory, TimeProvider.System));
    private static HttpResponseMessage Response(string body, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(body) };

    /// <summary>SystemOne requests carry only the bounded source, question contract, model and explicit keep-alive.</summary>
    [Fact]
    public async Task DecisionRequestUsesDedicatedRouteAndKeepAlive()
    {
        var paths = new List<string>();
        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Method == HttpMethod.Get) return Response("{\"models\":[]}");
            var body = Json(await request.Content!.ReadAsStringAsync(ct));
            body.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo("model", "state", "questions", "keep_alive");
            body.GetProperty("keep_alive").GetString().Should().Be("10s");
            body.GetProperty("state").GetString().Should().Be("Synthetic source only");
            return Response("{\"answers\":{\"q\":{\"type\":\"noul\",\"noul\":0.9}},\"usage\":{\"input_tokens\":20,\"output_tokens\":1}}");
        });
        var result = await Client(handler).DecideAsync(Request(), 120, default);
        result.Cold.Should().BeTrue();
        paths.Should().Equal("/api/ps", "/v1/systemone");
    }

    /// <summary>The conventional classifier cannot receive agent tools, RAG, context, or enabled thinking.</summary>
    [Fact]
    public async Task ConventionalCallIsFixedToolFreeAndStructured()
    {
        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/chat");
            var body = Json(await request.Content!.ReadAsStringAsync(ct));
            body.GetProperty("think").GetBoolean().Should().BeFalse();
            body.GetProperty("stream").GetBoolean().Should().BeFalse();
            body.TryGetProperty("tools", out _).Should().BeFalse();
            body.GetProperty("options").GetProperty("temperature").GetInt32().Should().Be(0);
            body.GetProperty("messages").GetArrayLength().Should().Be(2);
            body.GetProperty("messages")[1].GetProperty("content").GetString().Should().Be("Synthetic source only");
            return Response("{\"done\":true,\"done_reason\":\"stop\",\"message\":{\"content\":\"{\\\"q\\\":true}\"}}");
        });
        var response = await Client(handler).ClassifyAsync(Request() with { Model = "gemma4:12b" }, Json("{\"type\":\"object\"}"), default);
        response.Payload.GetProperty("q").GetBoolean().Should().BeTrue();
    }

    /// <summary>Explicit generic classification changes only the fixed instruction and bounded keep-alive.</summary>
    [Fact]
    public async Task GenericBenchmarkUsesItsRecordedInstructionAndBoundedResidency()
    {
        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            var body = Json(await request.Content!.ReadAsStringAsync(ct));
            body.GetProperty("messages")[0].GetProperty("content").GetString().Should().StartWith("Assess one explicit input.").And.NotContain("email");
            body.GetProperty("keep_alive").GetString().Should().Be("10s");
            body.GetProperty("think").GetBoolean().Should().BeFalse();
            body.TryGetProperty("tools", out _).Should().BeFalse();
            return Response("{\"done\":true,\"message\":{\"content\":\"{\\\"choice\\\":\\\"one\\\"}\"}}");
        });
        _ = await Client(handler).ClassifyAsync(Request() with { ClassificationInstructions = "Assess one explicit input. " }, Json("{}"), default);
    }

    /// <summary>Truncated, thinking-enabled and fenced responses fail without accepting an expensive alternate path.</summary>
    [Theory]
    [InlineData("{\"done\":false,\"message\":{\"content\":\"{}\"}}", "LlmOutputTruncation")]
    [InlineData("{\"done\":true,\"done_reason\":\"length\",\"message\":{\"content\":\"{}\"}}", "LlmOutputTruncation")]
    [InlineData("{\"done\":true,\"message\":{\"content\":\"{}\",\"thinking\":\"hidden reasoning\"}}", "IncompatibleModel")]
    [InlineData("{\"done\":true,\"message\":{\"content\":\"```json {} ```\"}}", "InvalidJson")]
    [InlineData("{\"done\":true,\"message\":{\"content\":\"\"}}", "EmptyResponse")]
    public async Task InvalidConventionalResponsesFail(string body, string code)
    {
        using var handler = new StubHttpMessageHandler((_, _) => Task.FromResult(Response(body)));
        var action = () => Client(handler).ClassifyAsync(Request(), Json("{}"), default);
        (await action.Should().ThrowAsync<LocalDecisionException>()).Which.Code.Should().Be(code);
    }

    /// <summary>Provider error bodies are never copied into exceptions and no redirect is followed.</summary>
    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ModelMissingOrRouteUnsupported")]
    [InlineData(HttpStatusCode.BadRequest, "IncompatibleModel")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "ProviderOverload")]
    [InlineData(HttpStatusCode.Redirect, "HttpFailure")]
    public async Task ProviderFailuresAreSanitized(HttpStatusCode status, string code)
    {
        var calls = 0;
        using var handler = new StubHttpMessageHandler((_, _) =>
        {
            calls++;
            var response = Response("PRIVATE PROVIDER ERROR BODY", status);
            response.Headers.Location = new Uri("https://remote.example.test");
            return Task.FromResult(response);
        });
        var action = () => Client(handler).ClassifyAsync(Request(), Json("{}"), default);
        var exception = (await action.Should().ThrowAsync<LocalDecisionException>()).Which;
        exception.Code.Should().Be(code);
        exception.ToString().Should().NotContain("PRIVATE PROVIDER");
        calls.Should().Be(1);
    }

    /// <summary>Explicit lifecycle release addresses only the dedicated model using keep-alive zero.</summary>
    [Fact]
    public async Task CleanupUsesLifecycleRequestForSelectedModelOnly()
    {
        using var handler = new StubHttpMessageHandler(async (request, ct) =>
        {
            request.RequestUri!.AbsolutePath.Should().Be("/api/generate");
            var body = Json(await request.Content!.ReadAsStringAsync(ct));
            body.GetProperty("model").GetString().Should().Be("tev1:latest");
            body.GetProperty("keep_alive").GetInt32().Should().Be(0);
            body.TryGetProperty("prompt", out _).Should().BeFalse();
            return Response("{}");
        });
        await Client(handler).UnloadAsync(Request().Endpoint, "tev1:latest", default);
    }

    /// <summary>Deletes isolated rendezvous files after all leases close.</summary>
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
