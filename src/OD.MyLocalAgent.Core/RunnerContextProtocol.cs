using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Encodes workflow-context snapshots on the private runner-to-UI output channel.
/// </summary>
public static class RunnerContextProtocol
{
    /// <summary>
    /// Gets the marker prefix used to distinguish context snapshots from normal output.
    /// </summary>
    public const string Prefix = "ODMLA_CONTEXT:";

    /// <summary>
    /// Encodes a JSON context snapshot as one output line.
    /// </summary>
    /// <param name="contextJson">The workflow context JSON.</param>
    /// <returns>The encoded protocol line.</returns>
    public static string Encode(string contextJson)
    {
        ArgumentNullException.ThrowIfNull(contextJson);
        return Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes(contextJson));
    }

    /// <summary>
    /// Attempts to decode a context protocol line.
    /// </summary>
    /// <param name="line">The runner output line.</param>
    /// <param name="contextJson">The decoded JSON when successful.</param>
    /// <returns><see langword="true"/> when the line contains a valid context snapshot.</returns>
    public static bool TryDecode(string line, out string contextJson)
    {
        ArgumentNullException.ThrowIfNull(line);
        contextJson = string.Empty;
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

            contextJson = candidate;
            return true;
        }
        catch (Exception exception) when (
            exception is FormatException or JsonException)
        {
            return false;
        }
    }
}
