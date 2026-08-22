using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>Represents a bounded, literal-only FTS5 query generated from caller text.</summary>
internal sealed record KnowledgeLexicalQuery(string MatchExpression, bool IsEmpty)
{
    /// <summary>Creates a deterministic OR query without exposing FTS5 syntax to callers.</summary>
    public static KnowledgeLexicalQuery Create(string input, KnowledgeOptions options)
    {
        var terms = new List<string>();
        var builder = new StringBuilder();
        foreach (var character in input.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(character) || character is '_' or '.' or '-' or ':') builder.Append(character);
            else { AddTerm(builder, terms, options); if (terms.Count == options.MaximumLexicalQueryTerms) break; }
        }
        if (terms.Count < options.MaximumLexicalQueryTerms) AddTerm(builder, terms, options);
        return terms.Count == 0
            ? new KnowledgeLexicalQuery(string.Empty, true)
            : new KnowledgeLexicalQuery(string.Join(" OR ", terms.Select(term => $"\"{term.Replace("\"", "\"\"")}\"")), false);
    }

    private static void AddTerm(StringBuilder builder, ICollection<string> terms, KnowledgeOptions options)
    {
        if (builder.Length > 0)
        {
            terms.Add(builder.ToString()[..Math.Min(builder.Length, options.MaximumLexicalTermCharacters)]);
            builder.Clear();
        }
    }
}
