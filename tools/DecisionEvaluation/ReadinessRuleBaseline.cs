using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace OD.MyLocalAgent.Evaluation;

/// <summary>A frozen lexical baseline for appointment fixtures, not a general natural-language policy engine.</summary>
public static partial class ReadinessRuleBaseline
{
    /// <summary>Evaluates facts with documented lexical limitations, without consulting labels or case identifiers.</summary>
    public static string Evaluate(string source)
    {
        var text = new string(source.Normalize(NormalizationForm.FormD).Where(c =>
            CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).ToLowerInvariant();
        text = InjectionTail().Replace(text, "");
        var consultation = text.Contains("consultation", StringComparison.Ordinal);
        var installation = text.Contains("installation", StringComparison.Ordinal);
        if (text.Contains("not installation", StringComparison.Ordinal) || text.Contains("pas une installation", StringComparison.Ordinal)) installation = false;
        if (text.Contains("repair", StringComparison.Ordinal) || text.Contains("reparation", StringComparison.Ordinal) ||
            (!consultation && !installation && !text.Contains("appointment", StringComparison.Ordinal) && !text.Contains("rendez-vous", StringComparison.Ordinal)) ||
            consultation && installation) return "review";
        var dates = Date().Matches(text).Select(m => m.Value).Distinct().ToList();
        if ((text.Contains("withdraw ", StringComparison.Ordinal) || text.Contains("retirez le ", StringComparison.Ordinal)) && dates.Count > 1) dates.RemoveAt(0);
        var times = Time().Matches(text).Select(m => m.Value).Distinct().ToArray();
        if (dates.Count > 1 || times.Length > 1) return "review";
        var hasTime = times.Length == 1 && !text.Contains("do not use", StringComparison.Ordinal) && !text.Contains("ne retenez pas", StringComparison.Ordinal);
        var hasContact = Contact().IsMatch(text) && !text.Contains("do not contact", StringComparison.Ordinal) && !text.Contains("ne contactez pas", StringComparison.Ordinal);
        return (consultation || installation) && dates.Count == 1 && hasTime && hasContact ? "process" : "clarify";
    }

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Date();
    [GeneratedRegex(@"\b\d{1,2}:\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Time();
    [GeneratedRegex(@"\b[\w.+-]+@[\w.-]+\.[a-z]{2,}\b", RegexOptions.CultureInvariant)]
    private static partial Regex Contact();
    [GeneratedRegex(@"(?:system:|systeme\s*:|developer instruction:|instruction developpeur\s*:|assistant\s*:|high priority system override:|priorite systeme maximale\s*:).*$", RegexOptions.CultureInvariant)]
    private static partial Regex InjectionTail();
}
