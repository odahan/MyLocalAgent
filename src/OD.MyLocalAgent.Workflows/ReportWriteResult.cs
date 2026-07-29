namespace OD.MyLocalAgent.Workflows;

/// <summary>
/// Represents a persisted execution report.
/// </summary>
/// <param name="ReportPath">The absolute Markdown report path.</param>
/// <param name="Markdown">The complete Markdown report.</param>
public sealed record ReportWriteResult(string ReportPath, string Markdown);
