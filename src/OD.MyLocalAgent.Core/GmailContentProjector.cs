using System.Globalization;
using System.Text.RegularExpressions;
using OD.MyLocalAgent.Connectors;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Creates the sole bounded canonical Gmail source admitted to Guardrails.
/// </summary>
public static partial class GmailContentProjector
{
    private const int FromMaximum = 512;
    private const int ToMaximum = 512;
    private const int SubjectMaximum = 1_000;
    private const int SnippetMaximum = 1_500;

    /// <summary>
    /// Projects a connector message deterministically without inspecting links or attachments.
    /// </summary>
    /// <param name="message">The connector message.</param>
    /// <param name="maximumCharacters">The complete canonical projection bound.</param>
    /// <returns>The bounded projection and omission metadata.</returns>
    public static GmailContentProjection Project(
        EmailMessageDto message,
        int maximumCharacters)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (maximumCharacters < 2_048)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumCharacters),
                "Gmail projections require at least 2048 characters.");
        }

        var originalFrom = Normalize(message.From);
        var originalTo = Normalize(message.To);
        var originalSubject = Normalize(message.Subject);
        var originalSnippet = Normalize(message.Snippet);
        var originalBody = Normalize(message.BodyPlain);
        var received = message.ReceivedAt.ToString("O", CultureInfo.InvariantCulture);
        var originalCanonical = BuildCanonical(
            originalFrom,
            originalTo,
            originalSubject,
            received,
            originalSnippet,
            originalBody);

        var from = Cap(originalFrom, FromMaximum);
        var to = Cap(originalTo, ToMaximum);
        var subject = Cap(originalSubject, SubjectMaximum);
        var snippet = Cap(originalSnippet, SnippetMaximum);

        var fields = new[] { from, to, subject, snippet };
        var originals = new[] { originalFrom, originalTo, originalSubject, originalSnippet };
        var reductionOrder = new[] { 3, 2, 1, 0 };
        var prefix = BuildPrefix(fields[0].Value, fields[1].Value, fields[2].Value, received, fields[3].Value);
        const int bodyMetadataReserve = 80;
        foreach (var fieldIndex in reductionOrder)
        {
            if (prefix.Length + "Body:\n".Length + bodyMetadataReserve <= maximumCharacters)
            {
                break;
            }

            var overflow = prefix.Length + "Body:\n".Length + bodyMetadataReserve - maximumCharacters;
            var minimum = OmissionMarker(originals[fieldIndex].Length).Length;
            var target = Math.Max(minimum, fields[fieldIndex].Value.Length - overflow);
            fields[fieldIndex] = Cap(originals[fieldIndex], target);
            prefix = BuildPrefix(fields[0].Value, fields[1].Value, fields[2].Value, received, fields[3].Value);
        }

        var bodyBudget = maximumCharacters - prefix.Length - "Body:\n".Length;
        var quoteMatch = ReplyBoundaryPattern().Match(originalBody);
        var quotedHistoryWasRemoved = quoteMatch.Success;
        var quotedOmitted = quoteMatch.Success ? originalBody.Length - quoteMatch.Index : 0;
        var currentBody = quoteMatch.Success ? originalBody[..quoteMatch.Index] : originalBody;
        var bodyMarkers = quoteMatch.Success ? QuotedHistoryMarker(quotedOmitted) : string.Empty;
        var bodyCapBudget = bodyBudget - (bodyMarkers.Length == 0 ? 0 : bodyMarkers.Length + 1);
        var body = Cap(currentBody, Math.Max(0, bodyCapBudget));
        var projectedBody = body.Value;
        if (bodyMarkers.Length > 0)
        {
            projectedBody = projectedBody.Length == 0
                ? bodyMarkers
                : projectedBody +
                    (projectedBody.EndsWith('\n') ? string.Empty : "\n") +
                    bodyMarkers;
        }

        var content = prefix + "Body:\n" + projectedBody;
        if (content.Length > maximumCharacters)
        {
            throw new InvalidOperationException("The Gmail projection exceeded its deterministic bound.");
        }

        var omitted = fields.Sum(static field => field.Omitted) + body.Omitted + quotedOmitted;
        return new GmailContentProjection(
            content,
            fields[0].Value,
            fields[1].Value,
            fields[2].Value,
            received,
            originalCanonical.Length,
            content.Length,
            omitted,
            omitted > 0,
            quotedHistoryWasRemoved);
    }

    private static string BuildCanonical(
        string from,
        string to,
        string subject,
        string received,
        string snippet,
        string body) =>
        BuildPrefix(from, to, subject, received, snippet) + "Body:\n" + body;

    private static string BuildPrefix(
        string from,
        string to,
        string subject,
        string received,
        string snippet) =>
        $"From: {from}\nTo: {to}\nSubject: {subject}\nReceived: {received}\nSnippet: {snippet}\n";

    private static CappedField Cap(string value, int maximum)
    {
        if (value.Length <= maximum)
        {
            return new CappedField(value, 0);
        }

        if (maximum <= 0)
        {
            return new CappedField(string.Empty, value.Length);
        }

        var omitted = value.Length;
        while (true)
        {
            var marker = OmissionMarker(omitted);
            if (marker.Length >= maximum)
            {
                return new CappedField(marker[..maximum], value.Length);
            }

            var prefixLength = maximum - marker.Length;
            var updated = value.Length - prefixLength;
            if (updated == omitted)
            {
                return new CappedField(value[..prefixLength] + marker, omitted);
            }

            omitted = updated;
        }
    }

    private static string OmissionMarker(int count) =>
        $"[... {count.ToString(CultureInfo.InvariantCulture)} characters omitted ...]";

    private static string QuotedHistoryMarker(int count) =>
        $"[... {count.ToString(CultureInfo.InvariantCulture)} quoted-history characters omitted ...]";

    private static string Normalize(string? value)
    {
        value ??= string.Empty;
        value = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n');
        var characters = value.Select(
            static character => character == '\n' || !char.IsControl(character)
                ? character
                : ' ').ToArray();
        var lines = new string(characters).Split('\n')
            .Select(static line => line.TrimEnd(' ', '\t'));
        var result = new List<string>();
        var blankCount = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blankCount++;
                if (blankCount > 2)
                {
                    continue;
                }
            }
            else
            {
                blankCount = 0;
            }

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    [GeneratedRegex(
        @"(?im)^(?:>+\s?|On\s+.+\s+wrote:\s*$|Le\s+.+\s+a\s+écrit\s*:\s*$|-{5,}\s*Original Message\s*-{5,}\s*$)",
        RegexOptions.CultureInvariant)]
    private static partial Regex ReplyBoundaryPattern();

    private readonly record struct CappedField(string Value, int Omitted);
}
