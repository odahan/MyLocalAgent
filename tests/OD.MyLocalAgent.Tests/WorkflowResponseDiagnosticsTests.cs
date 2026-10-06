using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Verifies rejected responses retain actionable, bounded diagnostics.</summary>
public sealed class WorkflowResponseDiagnosticsTests
{
    /// <summary>Distinguishes empty, malformed, and schema-invalid responses without relaxing validation.</summary>
    [Theory]
    [InlineData("", "EmptyResponse", "not valid JSON")]
    [InlineData("```json\n{}\n```", "InvalidJson", "not valid JSON")]
    [InlineData("{}", "SchemaMismatch", "$.category")]
    [InlineData("{\"category\":\"unknown\"}", "SchemaMismatch", "allowed enum")]
    public void FailureIncludesKindAndExactValidationError(string response, string failureKind, string error)
    {
        using var schema = JsonDocument.Parse(
            """{"type":"object","properties":{"category":{"type":"string","enum":["Important"]}},"required":["category"]}""");
        var action = () => WorkflowResponseSchemaValidator.ValidateResponse(response, schema.RootElement);

        var exception = action.Should().Throw<WorkflowStructuredResponseException>().Which;

        exception.Message.Should().Contain(error);
        using var diagnostic = JsonDocument.Parse(exception.ResponseDiagnostics!);
        diagnostic.RootElement.GetProperty("failureKind").GetString().Should().Be(failureKind);
        diagnostic.RootElement.GetProperty("responseCharacters").GetInt32().Should().Be(response.Length);
        diagnostic.RootElement.GetProperty("responseHead").GetString().Should().Be(response);
    }

    /// <summary>Preserves both ends of a long response, its hash, and parser location on one log line.</summary>
    [Fact]
    public void LongMalformedResponseHasBoundedEscapedExcerpts()
    {
        using var schema = JsonDocument.Parse("""{"type":"object"}""");
        var response = "not-json\n" + new string('x', 10000) + "END";
        var action = () => WorkflowResponseSchemaValidator.ValidateResponse(response, schema.RootElement);

        var exception = action.Should().Throw<WorkflowStructuredResponseException>().Which;

        exception.InnerException.Should().BeAssignableTo<JsonException>();
        exception.Message.Should().Contain("LineNumber").And.Contain("BytePositionInLine");
        exception.ResponseDiagnostics.Should().NotContain("\n");
        using var diagnostic = JsonDocument.Parse(exception.ResponseDiagnostics!);
        var root = diagnostic.RootElement;
        root.GetProperty("responseHead").GetString()!.Length.Should().Be(1536);
        root.GetProperty("responseTail").GetString()!.Length.Should().Be(512);
        root.GetProperty("responseTail").GetString().Should().EndWith("END");
        root.GetProperty("excerptTruncated").GetBoolean().Should().BeTrue();
        root.GetProperty("responseSha256").GetString().Should().Be(
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(response))));
    }
}
