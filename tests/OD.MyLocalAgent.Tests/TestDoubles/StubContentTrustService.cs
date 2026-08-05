using OD.MyLocalAgent.Guardrails;

namespace OD.MyLocalAgent.Tests.TestDoubles;

internal sealed class StubContentTrustService : IContentTrustService
{
    public Func<string, bool, CancellationToken, Task<ContentTrustResult>>?
        ScreenHandler { get; set; }

    public Func<string, int, CancellationToken, Task<ContentSummaryResult>>?
        SummarizeHandler { get; set; }

    public Func<IReadOnlyList<CompactExternalContentItem>, CancellationToken,
        Task<CompactExternalContentBatchResult>>? CompactHandler { get; set; }

    public Func<IReadOnlyList<CompactExternalContentItem>, CancellationToken,
        Task<CompactExternalContentBatchResult>>? ScreenCompactHandler { get; set; }

    public List<string> ScreenedContents { get; } = [];

    public List<string> SummarizedContents { get; } = [];

    public List<IReadOnlyList<CompactExternalContentItem>> CompactBatches { get; } = [];

    public List<IReadOnlyList<CompactExternalContentItem>> ScreenBatches { get; } = [];

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

    public Task<CompactExternalContentBatchResult> ProcessCompactAsync(
        IReadOnlyList<CompactExternalContentItem> items,
        CancellationToken cancellationToken)
    {
        CompactBatches.Add(items.ToArray());
        if (CompactHandler is not null)
        {
            return CompactHandler(items, cancellationToken);
        }

        var results = items.Select(
            item =>
            {
                var summary = item.Content.Length <= item.MaximumSummaryCharacters
                    ? item.Content
                    : item.Content[..item.MaximumSummaryCharacters];
                var sourceTrust = CreateTrust(item.Content);
                return new CompactExternalContentResult(
                    item.Id,
                    summary,
                    sourceTrust,
                    CreateTrust(summary),
                    CompactExternalContentStatus.Summarized,
                    false,
                    0);
            }).ToArray();
        return Task.FromResult(new CompactExternalContentBatchResult(results, []));
    }

    public Task<CompactExternalContentBatchResult> ScreenCompactAsync(
        IReadOnlyList<CompactExternalContentItem> items,
        CancellationToken cancellationToken)
    {
        ScreenBatches.Add(items.ToArray());
        if (ScreenCompactHandler is not null)
        {
            return ScreenCompactHandler(items, cancellationToken);
        }

        var results = items.Select(
            item => new CompactExternalContentResult(
                item.Id,
                null,
                CreateTrust(item.Content),
                null,
                CompactExternalContentStatus.Screened,
                false,
                0)).ToArray();
        return Task.FromResult(new CompactExternalContentBatchResult(results, []));
    }

    private static ContentTrustResult CreateTrust(string content) =>
        new(
            100,
            ContentTrustEnvelope.WrapPlaintext(content),
            "none",
            ContentTrustStatus.Screened,
            [],
            "Stub",
            0);
}
