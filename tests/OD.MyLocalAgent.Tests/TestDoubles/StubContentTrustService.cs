using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubContentTrustService : IContentTrustService
{
    public Func<string, bool, CancellationToken, Task<ContentTrustResult>>?
        ScreenHandler { get; set; }

    public Func<string, int, CancellationToken, Task<ContentSummaryResult>>?
        SummarizeHandler { get; set; }

    public List<string> ScreenedContents { get; } = [];

    public List<string> SummarizedContents { get; } = [];

    public Task<ContentTrustResult> ScreenAsync(
        string content,
        bool returnBase64,
        CancellationToken cancellationToken)
    {
        ScreenedContents.Add(content);
        if (ScreenHandler is not null)
        {
            return ScreenHandler(content, returnBase64, cancellationToken);
        }

        return Task.FromResult(
            new ContentTrustResult(
                100,
                returnBase64
                    ? ContentTrustEnvelope.WrapBase64(content)
                    : ContentTrustEnvelope.WrapPlaintext(content),
                returnBase64 ? "base64" : "none",
                ContentTrustStatus.Screened,
                [],
                "Stub",
                0));
    }

    public Task<ContentSummaryResult> SummarizeAsync(
        string content,
        int maxSummaryChars,
        CancellationToken cancellationToken)
    {
        SummarizedContents.Add(content);
        if (SummarizeHandler is not null)
        {
            return SummarizeHandler(content, maxSummaryChars, cancellationToken);
        }

        var summary = content.Length <= maxSummaryChars
            ? content
            : content[..maxSummaryChars];
        return Task.FromResult(
            new ContentSummaryResult(
                ContentSummaryStatus.Summarized,
                summary,
                content.Length,
                summary.Length,
                100,
                [],
                "Stub",
                0));
    }
}
