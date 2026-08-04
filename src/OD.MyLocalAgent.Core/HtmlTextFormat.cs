using System.Text.Json.Serialization;

namespace OD.MyLocalAgent.Core;

/// <summary>
/// Selects the output format produced from an HTML document.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<HtmlTextFormat>))]
public enum HtmlTextFormat
{
    /// <summary>
    /// Produces normalized plain text.
    /// </summary>
    Text,

    /// <summary>
    /// Produces Markdown preserving principal document structure.
    /// </summary>
    Markdown,
}
