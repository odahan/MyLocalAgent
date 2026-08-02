using System.Globalization;
using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Projects workflow prompt text as a deterministic single-line preview.
/// </summary>
/// <remarks>
/// The projection is display-only. It is never written back to prompt text, so the workflow
/// prompt keeps its exact characters.
/// </remarks>
public static class WorkflowPromptPreviewProjection
{
    /// <summary>
    /// Gets the maximum number of text elements kept before truncation.
    /// </summary>
    public const int MaximumTextElements = 120;

    /// <summary>
    /// Gets the marker appended when the preview is truncated.
    /// </summary>
    public const string Ellipsis = "…";

    private const string LineFeedMarker = " ⏎ ";
    private const string TabMarker = " ⇥ ";

    /// <summary>
    /// Creates the single-line preview of a prompt.
    /// </summary>
    /// <param name="promptText">The LF-canonical prompt working text.</param>
    /// <returns>The deterministic preview.</returns>
    public static string Create(string? promptText)
    {
        if (string.IsNullOrEmpty(promptText))
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var enumerator = StringInfo.GetTextElementEnumerator(promptText);
        var kept = 0;
        while (enumerator.MoveNext())
        {
            if (kept == MaximumTextElements)
            {
                return builder.Append(Ellipsis).ToString();
            }

            builder.Append(Project((string)enumerator.Current));
            kept++;
        }

        return builder.ToString();
    }

    private static string Project(string textElement)
    {
        if (textElement.Length != 1)
        {
            return textElement;
        }

        return textElement[0] switch
        {
            '\n' => LineFeedMarker,
            '\t' => TabMarker,
            // Remaining C0 controls and DEL use their Unicode control picture.
            <= '\u001f' and var control => ((char)('\u2400' + control)).ToString(),
            '\u007f' => "\u2421",
            var other => other.ToString(),
        };
    }
}
