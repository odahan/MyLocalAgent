using System.Text;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

internal sealed record GmailDecisionProjection(string DecisionSource, string LlmSource, bool CurrentContentTruncated,
    bool QuotedHistoryRemoved, int TokenEstimate, int CharacterCount)
{
    internal static int MailBudget(DecisionQuestionSet questions)
    {
        var budget = 2050 - 400 - EstimateTokens(questions.Json.GetRawText());
        if (budget < 128)
            throw new WorkflowValidationException("Question contract leaves fewer than 128 conservative mail tokens in the 2050-token decision context. Shorten the questions.");
        return budget;
    }

    /// <summary>
    /// Uses one token per UTF-8 byte as a conservative upper bound for byte-fallback BPE models.
    /// It reserves 400 tokens for templates, option scoring, and output; it never assumes characters divided by four.
    /// </summary>
    internal static int EstimateTokens(string text) => Encoding.UTF8.GetByteCount(text);

    internal static GmailDecisionProjection Create(EmailMessageDto mail, int maximumCharacters, int tokenBudget)
    {
        var projected = GmailContentProjector.Project(mail with
        {
            From = Bound(mail.From, 256), To = "", Subject = Bound(mail.Subject, 300), Snippet = "",
        }, maximumCharacters);
        var llm = TruncateBytes(projected.Content, 8192 - 1200 - (2050 - 400 - tokenBudget), out var llmTruncated);
        var source = TruncateBytes(llm, tokenBudget, out var truncated);
        var currentTruncated = truncated || llmTruncated || System.Text.RegularExpressions.Regex.IsMatch(projected.Content,
            @"\[\.\.\. \d+ characters omitted \.\.\.\]", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        return new GmailDecisionProjection(source, llm, currentTruncated, projected.QuotedHistoryWasRemoved,
            EstimateTokens(source), source.Length);
    }

    internal static string Bound(string value, int maximum) => value.Length <= maximum ? value :
        value[..(char.IsHighSurrogate(value[maximum - 1]) ? maximum - 1 : maximum)];

    private static string TruncateBytes(string value, int maximum, out bool truncated)
    {
        truncated = EstimateTokens(value) > maximum;
        if (!truncated) return value;
        const string marker = "\n[Current content omitted]";
        var remaining = maximum - EstimateTokens(marker);
        var builder = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Utf8SequenceLength > remaining) break;
            builder.Append(rune.ToString());
            remaining -= rune.Utf8SequenceLength;
        }
        return builder + marker;
    }
}
