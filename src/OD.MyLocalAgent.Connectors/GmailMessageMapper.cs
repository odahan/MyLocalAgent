using Google.Apis.Gmail.v1.Data;
using System.Text;

namespace OD.MyLocalAgent.Connectors;

/// <summary>
/// Maps Gmail API messages to the connector's stable email data transfer object.
/// </summary>
internal static class GmailMessageMapper
{
    public static EmailMessageDto Map(Message message)
    {
        ArgumentNullException.ThrowIfNull(message);
        var headers = message.Payload?.Headers ?? [];
        return new EmailMessageDto(
            message.Id ?? string.Empty,
            GetHeader(headers, "From"),
            GetHeader(headers, "To"),
            GetHeader(headers, "Subject"),
            message.Snippet ?? string.Empty,
            ExtractPlainText(message.Payload),
            ParseReceivedAt(message.InternalDate),
            message.LabelIds?.Contains("UNREAD", StringComparer.OrdinalIgnoreCase) == true);
    }

    private static string GetHeader(IEnumerable<MessagePartHeader> headers, string name)
    {
        return headers.FirstOrDefault(
            header => string.Equals(header.Name, name, StringComparison.OrdinalIgnoreCase))?.Value
            ?? string.Empty;
    }

    private static string ExtractPlainText(MessagePart? root)
    {
        if (root is null)
        {
            return string.Empty;
        }

        var plainPart = EnumerateParts(root).FirstOrDefault(
            part => string.Equals(part.MimeType, "text/plain", StringComparison.OrdinalIgnoreCase) &&
                    !string.IsNullOrWhiteSpace(part.Body?.Data));
        if (plainPart is not null)
        {
            return DecodeBase64Url(plainPart.Body!.Data);
        }

        return !string.IsNullOrWhiteSpace(root.Body?.Data)
            ? DecodeBase64Url(root.Body.Data)
            : string.Empty;
    }

    private static IEnumerable<MessagePart> EnumerateParts(MessagePart part)
    {
        yield return part;
        if (part.Parts is null)
        {
            yield break;
        }

        foreach (var child in part.Parts)
        {
            foreach (var descendant in EnumerateParts(child))
            {
                yield return descendant;
            }
        }
    }

    private static string DecodeBase64Url(string encoded)
    {
        var normalized = encoded.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(
            normalized.Length + ((4 - normalized.Length % 4) % 4),
            '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }

    private static DateTimeOffset ParseReceivedAt(long? internalDate)
    {
        return internalDate is > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(internalDate.Value)
            : DateTimeOffset.UnixEpoch;
    }
}
