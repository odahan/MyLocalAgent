using System.Text.Json;
using Microsoft.Extensions.AI;

namespace OD.MyLocalAgent.Guardrails;

/// <summary>Constrains provider output to the contracts parsed by Guardrails.</summary>
internal static class GuardrailResponseSchemas
{
    public static ChatResponseFormat Resolve(string systemPrompt)
    {
        const string verdict = """{"type":"object","additionalProperties":false,"required":["score","indicators"],"properties":{"score":{"type":"integer","minimum":0,"maximum":100},"indicators":{"type":"array","items":{"type":"string"}}}}""";
        const string summary = """{"type":"object","additionalProperties":false,"required":["summary"],"properties":{"summary":{"type":"string"}}}""";
        const string batchVerdict = """{"type":"object","additionalProperties":false,"required":["results"],"properties":{"results":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","score","indicators"],"properties":{"id":{"type":"string"},"score":{"type":"integer","minimum":0,"maximum":100},"indicators":{"type":"array","items":{"type":"string"}}}}}}}""";
        const string batchSummary = """{"type":"object","additionalProperties":false,"required":["results"],"properties":{"results":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["id","summary"],"properties":{"id":{"type":"string"},"summary":{"type":"string"}}}}}}""";
        var schema = systemPrompt switch
        {
            ContentScreenerPrompt.System => verdict,
            ContentSummarizerPrompt.System => summary,
            CompactContentBatchScreenerPrompt.System => batchVerdict,
            CompactContentBatchSummarizerPrompt.System => batchSummary,
            _ => throw new InvalidOperationException("Unknown Guardrails response contract."),
        };
        using var document = JsonDocument.Parse(schema);
        return ChatResponseFormat.ForJsonSchema(document.RootElement.Clone(), "guardrail_response");
    }
}
