using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Encodes opt-in detailed Runner diagnostics on the private Runner-to-UI output channel.
/// </summary>
public static class RunnerDebugProtocol
{
    /// <summary>
    /// Gets the marker prefix used to distinguish detailed diagnostic events.
    /// </summary>
    public const string Prefix = "ODMLA_DEBUG:";

    /// <summary>
    /// Encodes one diagnostic event as a single output line.
    /// </summary>
    /// <param name="category">The event category.</param>
    /// <param name="name">The operation name.</param>
    /// <param name="details">The diagnostic details.</param>
    /// <returns>The encoded protocol line.</returns>
    public static string Encode(string category, string name, object? details)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var json = JsonSerializer.Serialize(
            new { category, name, details },
            ToolJson.Options);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>
    /// Attempts to decode one diagnostic event.
    /// </summary>
    /// <param name="line">The candidate Runner output line.</param>
    /// <param name="diagnosticJson">The formatted diagnostic JSON when successful.</param>
    /// <returns><see langword="true"/> when the line contains a valid diagnostic event.</returns>
    public static bool TryDecode(string line, out string diagnosticJson)
    {
        ArgumentNullException.ThrowIfNull(line);
        diagnosticJson = string.Empty;
        if (!line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var candidate = Encoding.UTF8.GetString(
                Convert.FromBase64String(line[Prefix.Length..]));
            using var document = JsonDocument.Parse(candidate);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            diagnosticJson = JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions(ToolJson.Options) { WriteIndented = true });
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            return false;
        }
    }
}
