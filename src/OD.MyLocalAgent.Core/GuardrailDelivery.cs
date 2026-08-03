using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Core;

internal static class GuardrailDelivery
{
    public static ProtectedContent Apply(
        string canonicalContent,
        ContentTrustResult result,
        GuardrailsOptions options,
        bool forceBase64 = false)
    {
        var disposition = ContentTrustPolicy.Decide(result, options);
        if (forceBase64 && disposition == TrustDisposition.PassThrough)
        {
            disposition = TrustDisposition.EncodeBase64;
        }

        return disposition switch
        {
            TrustDisposition.PassThrough => new ProtectedContent(
                ContentTrustEnvelope.WrapPlaintext(canonicalContent),
                "none"),
            TrustDisposition.EncodeBase64 => new ProtectedContent(
                ContentTrustEnvelope.WrapBase64(canonicalContent),
                "base64"),
            TrustDisposition.Withhold => new ProtectedContent(
                $"[external content withheld by guardrails: trust score {result.TrustScore}]",
                "none"),
            _ => throw new InvalidOperationException(
                $"Unsupported trust disposition '{disposition}'."),
        };
    }

    public static IReadOnlyList<string> UnionIndicators(
        IEnumerable<string> first,
        IEnumerable<string> second)
    {
        var result = new List<string>();
        foreach (var indicator in first.Concat(second))
        {
            if (result.Count >= 20)
            {
                break;
            }

            if (!result.Contains(indicator, StringComparer.Ordinal))
            {
                result.Add(indicator);
            }
        }

        return result;
    }

    internal sealed record ProtectedContent(string Content, string ContentEncoding);
}
