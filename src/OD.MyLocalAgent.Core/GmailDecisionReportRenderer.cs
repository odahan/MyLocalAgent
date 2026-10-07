using System.Globalization;
using System.Text;
using System.Text.Json;

namespace OD.MyLocalAgent.Core;

/// <summary>Validates coverage and renders mail rows deterministically, escaping all untrusted display values.</summary>
public static class GmailDecisionReportRenderer
{
    /// <summary>Creates a row-bearing Markdown report without network, model, or file access.</summary>
    public static string Render(string source, string title = "Gmail watch", bool includeNoise = true)
    {
        if (title.Length > 200 || string.IsNullOrWhiteSpace(title) || Encoding.UTF8.GetByteCount(source) > 512 * 1024)
            throw new WorkflowValidationException("Report title or source exceeds its bound.");
        GmailDecisionResult result;
        try
        {
            using var document = JsonDocument.Parse(source);
            result = DecisionContractJson.Read<GmailDecisionResult>(document.RootElement);
            Validate(result);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException or NullReferenceException)
        { throw new WorkflowValidationException("Invalid Gmail decision report source."); }
        var b = new StringBuilder();
        b.Append("# ").AppendLine(Escape(title)).AppendLine();
        b.Append("Collected: ").AppendLine(result.CollectedAt.ToString("O", CultureInfo.InvariantCulture));
        b.Append("Selection: ").Append(Escape(result.Selection.Kind));
        if (result.Selection.DaysBack is { } days) b.Append("; days back: ").Append(days);
        b.AppendLine().AppendLine();
        b.Append("Coverage: ").Append(result.Counts.Collected).Append(" collected; ").Append(result.Counts.Processed)
            .Append(" processed; ").Append(result.Counts.Classified).Append(" classified; ").Append(result.Counts.Provisional)
            .Append(" provisional; ").Append(result.Counts.Unresolved).AppendLine(" unresolved.");
        if (result.CollectionPartial) b.AppendLine("**Collection is partial: additional messages were excluded by the collection bound.**");
        b.Append("Status: ").AppendLine(Escape(result.Status));
        b.Append("Classification mode: ").AppendLine(Escape(result.ClassificationMode));
        if (result.SchemaVersion == 2)
            b.Append("Engine: ").Append(Escape(result.Engine!)).Append("; provider: ").Append(Escape(result.Provider!))
                .Append("; acceptance basis: ").Append(Escape(result.AcceptanceBasis!)).AppendLine()
                .AppendLine(result.Capabilities!.Scores ? "Native scores are available." : "Scores unavailable. Rubric levels are semantic values, not probabilities.");
        b.Append("Local models: ").AppendLine(string.Join(", ", result.EffectiveModels.Select(p => Escape(p.Key) + ": " + Escape(p.Value))));
        b.Append("Second opinions: ").Append(result.Counts.ArbitrationAttempted).Append(" attempted, ")
            .Append(result.Counts.ArbitrationResolved).Append(" resolved. Lightweight fallback: ").Append(result.Counts.LightweightFallbackResolved).AppendLine(" resolved.");
        if (result.ClassificationMode == "LlmOnly")
            b.Append("Direct LLM: ").Append(result.Counts.DirectLlmResolved).AppendLine(" resolved; native decisions and arbitration are bypassed.");
        if (result.Messages.Count > 0 && result.Messages.All(m =>
                m.Projection.LlmContentTruncated.HasValue))
        {
            b.Append("Source omissions: decision ").Append(result.ClassificationMode == "LlmOnly" ? "not used" :
                    result.Messages.Count(m => m.Projection.DecisionContentTruncated == true).ToString(CultureInfo.InvariantCulture))
                .Append("; LLM ").Append(result.Messages.Count(m =>
                    m.DecisionSource is "arbitrated" or "lightweightLlm" or "directLlm" && m.Projection.LlmContentTruncated == true))
                .Append("; quoted history removed ").Append(result.Messages.Count(m => m.Projection.QuotedHistoryRemoved))
                .AppendLine(" (separate from current content truncation).");
        }
        b.Append("Timing: collection ").Append(Milliseconds(result.CollectionMs)).Append("; gate wait ")
            .Append(Milliseconds(result.Messages.Sum(m => m.Timing.GateWaitMs))).Append("; decision load + inference ")
            .Append(Milliseconds(result.Messages.Sum(m => m.Timing.DecisionMs))).Append("; second opinion ")
            .Append(Milliseconds(result.Messages.Sum(m => m.Timing.ArbitrationMs))).Append("; fallback ")
            .Append(Milliseconds(result.Messages.Sum(m => m.Timing.FallbackMs))).Append("; total ").Append(Milliseconds(result.TotalMs)).AppendLine(".");
        if (result.ClassificationMode == "LlmOnly")
            b.Append("Direct LLM load + inference: ").Append(Milliseconds(result.Messages.Sum(m => m.Timing.ClassificationMs))).AppendLine(".");
        foreach (var warning in result.Warnings) b.Append("**Warning: ").Append(Escape(warning)).AppendLine("**");
        b.AppendLine();
        if (result.Messages.Count == 0) return b.AppendLine("**No messages found.**").ToString();
        var hidden = 0;
        foreach (var category in result.ReportMapping.Categories.OrderByDescending(p => p.Value.AttentionRank))
        {
            var rows = result.Messages.Where(r => r.Category == category.Key).ToArray();
            if (rows.Length == 0) continue;
            if (!includeNoise && category.Value.AttentionRank == 0)
            {
                hidden += rows.Length;
                continue;
            }
            b.Append("## ").Append(Escape(category.Value.Label)).Append(" (").Append(rows.Length).AppendLine(")").AppendLine();
            foreach (var row in rows) Row(b, row, result.ReportMapping);
            b.AppendLine();
        }
        var unresolved = result.Messages.Where(r => r.Category is null).ToArray();
        if (unresolved.Length > 0)
        {
            b.Append("## Unresolved (").Append(unresolved.Length).AppendLine(")").AppendLine();
            foreach (var row in unresolved) Row(b, row, result.ReportMapping);
        }
        if (hidden > 0) b.AppendLine().Append(hidden).AppendLine(" newsletters/advertising rows are intentionally hidden (includeNoise = false).");
        return b.ToString();
    }

    private static void Validate(GmailDecisionResult r)
    {
        if ((r.SchemaVersion == 1 ? r.ClassificationMode is not ("Hybrid" or "LlmOnly") :
                r.SchemaVersion != 2 || r.ClassificationMode != "Global" || r.Engine is not ("CentralLlm" or "NativeDecision") ||
                r.Capabilities?.Scores != (r.Engine == "NativeDecision") || string.IsNullOrWhiteSpace(r.Provider) ||
                r.AcceptanceBasis != (r.Engine == "NativeDecision" ? "nativeThresholds" : "validatedChoice")) ||
            r.Status is not ("Completed" or "CompletedWithWarnings" or "Partial") ||
            r.Messages.Count > 500 || r.QuestionDefinitions.Count is < 1 or > 8 ||
            r.Messages.Select(m => m.Id).Distinct(StringComparer.Ordinal).Count() != r.Messages.Count ||
            r.Counts != GmailDecisionTool.Count(r.Messages) || !r.QuestionDefinitions.TryGetValue(r.ReportMapping.CategoryQuestion, out var category) ||
            category.Type != "choice" || r.ReportMapping.Categories.Count != category.Keys.Count ||
            r.ReportMapping.Categories.Keys.Any(k => !category.Keys.Contains(k)) ||
            r.ReportMapping.Categories.Values.Any(c => c.Label.Length is < 1 or > 100 || c.AttentionRank is < 0 or > 100) ||
            r.Selection.Kind is not ("Today" or "Unread" or "Recent"))
            throw new WorkflowValidationException("Invalid decision report version, mapping, identities, or count invariants.");
        foreach (var definition in r.QuestionDefinitions.Values)
        {
            if (definition.Type is not ("choice" or "noul" or "score") ||
                (definition.Type == "choice" && definition.Keys.Count is < 2 or > 24) ||
                (definition.Type == "score" && definition.Keys.Count is < 2 or > 10))
                throw new WorkflowValidationException("Invalid decision report answer definitions.");
        }
        if ((r.ReportMapping.ReplyQuestion is { } reply && (!r.QuestionDefinitions.TryGetValue(reply, out var replyDefinition) || replyDefinition.Type != "noul")) ||
            (r.ReportMapping.PriorityQuestion is { } priority && (!r.QuestionDefinitions.TryGetValue(priority, out var priorityDefinition) || priorityDefinition.Type != "score")) ||
            !double.IsFinite(r.ReportMapping.ReplyTrueAtPercent) || r.ReportMapping.ReplyTrueAtPercent is < 0 or > 100 ||
            !double.IsFinite(r.CollectionMs) || r.CollectionMs < 0 || !double.IsFinite(r.TotalMs) || r.TotalMs < 0)
            throw new WorkflowValidationException("Invalid report reply/urgency mapping or operation timing.");
        foreach (var row in r.Messages)
        {
            if (row.Id.Length is < 1 or > 256 || row.Sender.Length > 256 || row.Subject.Length > 300 ||
                row.Status is not ("Classified" or "Provisional" or "Unresolved") ||
                row.DecisionSource is not ("decision" or "arbitrated" or "lightweightLlm" or "directLlm" or "none") ||
                (r.ClassificationMode == "LlmOnly" && (row.DecisionSource is not ("directLlm" or "none") || row.ArbitrationAttempted || row.DecisionScores.Count != 0)) ||
                (r.ClassificationMode == "Hybrid" && row.DecisionSource == "directLlm") ||
                (r.SchemaVersion == 2 && (row.ArbitrationAttempted || row.DecisionSource is "arbitrated" or "lightweightLlm" ||
                    (r.Engine == "CentralLlm" && (row.DecisionScores.Count != 0 || row.DecisionSource == "decision")) ||
                    (r.Engine == "NativeDecision" && row.DecisionSource == "directLlm"))) ||
                row.RequiresHumanReview != (row.Status != "Classified") ||
                row.GmailUrl != GmailDecisionTool.GmailUrl(row.Id) ||
                (row.Category is not null && !r.ReportMapping.Categories.ContainsKey(row.Category)) ||
                (row.Status != "Unresolved" && (!row.Processed || row.DecisionSource == "none")) ||
                (row.Status == "Unresolved" && (row.Answers.Count != 0 || row.Category is not null)) ||
                (row.Status != "Unresolved" && (row.Category is null || row.Answers.Count != r.QuestionDefinitions.Count)))
                throw new WorkflowValidationException("Invalid decision report row metadata or status.");
            foreach (var (key, answer) in row.Answers)
            {
                if (!r.QuestionDefinitions.TryGetValue(key, out var q) || answer is not JsonElement value)
                    throw new WorkflowValidationException("Invalid decision report answer.");
                var valid = q.Type switch
                {
                    "choice" => value.ValueKind == JsonValueKind.String && q.Keys.Contains(value.GetString()!),
                    "noul" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
                    "score" => value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var n) && n >= 0 && n < q.Keys.Count,
                    _ => false,
                };
                if (!valid) throw new WorkflowValidationException("Decision report contains an incompatible answer.");
            }
            if (row.Category is not null && (!row.Answers.TryGetValue(r.ReportMapping.CategoryQuestion, out var a) ||
                ((JsonElement)a).GetString() != row.Category))
                throw new WorkflowValidationException("Decision report category does not match its answer.");
            foreach (var duration in new[] { row.Timing.GateWaitMs, row.Timing.DecisionMs, row.Timing.ArbitrationMs, row.Timing.FallbackMs, row.Timing.ClassificationMs })
                if (!double.IsFinite(duration) || duration < 0) throw new WorkflowValidationException("Invalid decision report timing.");
            foreach (var score in row.DecisionScores.Values)
            {
                if (score.Top is < 0 or > 1 || score.Margin is < 0 or > 1 ||
                    !double.IsFinite(score.Top) || !double.IsFinite(score.Margin) ||
                    (score.Noul is { } n && (!double.IsFinite(n) || n is < 0 or > 1)) ||
                    (score.Confidence is { } c && (!double.IsFinite(c) || c is < 0 or > 1)) ||
                    (score.Score is { } s && (!double.IsFinite(s) || s is < 0 or > 9)) ||
                    (score.Probabilities is { } probabilities && (probabilities.Values.Any(v => !double.IsFinite(v) || v is < 0 or > 1) || Math.Abs(probabilities.Values.Sum() - 1) > 0.01)))
                    throw new WorkflowValidationException("Invalid report native score details.");
            }
        }
    }

    private static void Row(StringBuilder b, DecisionMailResult row, ReportMapping mapping)
    {
        b.Append("- **").Append(Escape(string.IsNullOrWhiteSpace(row.Subject) ? "(No subject)" : row.Subject)).Append("** — ")
            .Append(Escape(row.Sender)).Append(" — ").Append(row.ReceivedAt.ToString("O", CultureInfo.InvariantCulture));
        if (row.GmailUrl is not null) b.Append(" — [Open in Gmail](").Append(row.GmailUrl).Append(')');
        if (mapping.ReplyQuestion is { } reply && row.Answers.TryGetValue(reply, out var answer))
            b.Append(" — Reply/action: ").Append(((JsonElement)answer).GetBoolean() ? "Yes" : "No");
        if (mapping.PriorityQuestion is { } priority && row.Answers.TryGetValue(priority, out var level))
            b.Append(" — Urgency level: ").Append(((JsonElement)level).GetInt32());
        if (row.RequiresHumanReview) b.Append(" — **Human review: ").Append(row.Status).Append("**");
        if (row.Disagreement) b.Append(" — **Models disagree**");
        if (row.ArbitrationReasons.Contains("VisibilityProtectionOverride"))
            b.Append(" — **Visibility protection: retained native category; verify in Gmail**");
        if (row.DecisionSource is "arbitrated" or "lightweightLlm" or "directLlm" && row.Projection.LlmContentTruncated == true)
            b.Append(" — **LLM source truncated**");
        b.AppendLine();
    }

    private static string Milliseconds(double value) => value.ToString("0", CultureInfo.InvariantCulture) + " ms";

    /// <summary>Escapes Markdown, HTML, line breaks, and URL separators in untrusted display text.</summary>
    public static string Escape(string value)
    {
        var b = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value < 32 || rune.Value == 127) { b.Append(' '); continue; }
            if (rune.Value < 128 && "\\`*_{}[]()#+-.!|<>:&@/=".Contains((char)rune.Value))
                b.Append("&#").Append(rune.Value).Append(';');
            else b.Append(rune.ToString());
        }
        return b.ToString();
    }
}
