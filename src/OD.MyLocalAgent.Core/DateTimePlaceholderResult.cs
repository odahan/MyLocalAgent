namespace OD.MyLocalAgent.Core;

/// <summary>
/// Represents the content-free outcome of resolving a date/time placeholder body.
/// </summary>
/// <param name="Status">The parsing and formatting status.</param>
/// <param name="Value">The invariant formatted value when resolution succeeded.</param>
public sealed record DateTimePlaceholderResult(
    DateTimePlaceholderStatus Status,
    string? Value);
