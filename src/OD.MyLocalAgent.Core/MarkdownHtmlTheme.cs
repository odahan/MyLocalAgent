using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Selects the standalone color theme used for Markdown HTML output.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<MarkdownHtmlTheme>))]
public enum MarkdownHtmlTheme
{
    /// <summary>
    /// Uses the dark standalone document palette.
    /// </summary>
    Dark,

    /// <summary>
    /// Uses the light standalone document palette.
    /// </summary>
    Light,
}
