using System.Text;

namespace OD.MyLocalAgent.Core;

/// <summary>Normalizes and validates Knowledge authorization tags.</summary>
public static class KnowledgeTagPolicy
{
    /// <summary>Gets the maximum number of tags in an owning scope.</summary>
    public const int MaximumScopeTags = 64;

    /// <summary>Gets the maximum number of tags supplied by one tool call.</summary>
    public const int MaximumCallTags = 16;

    /// <summary>Normalizes one tag using invariant, case-insensitive semantics.</summary>
    public static string Normalize(string tag)
    {
        ArgumentNullException.ThrowIfNull(tag);
        var builder = new StringBuilder(tag.Length);
        var pendingSpace = false;
        foreach (var character in tag.Trim())
        {
            if (char.IsControl(character))
            {
                throw new ArgumentException("Knowledge tags cannot contain control characters.", nameof(tag));
            }

            if (char.IsWhiteSpace(character))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        var normalized = builder.ToString();
        if (normalized.Length is < 1 or > 64 || normalized == "*")
        {
            throw new ArgumentException("Knowledge tags must contain 1 to 64 visible characters and cannot be '*'.", nameof(tag));
        }

        return normalized;
    }

    /// <summary>Validates and normalizes a tag collection while rejecting duplicates.</summary>
    public static IReadOnlyList<string> NormalizeScope(IEnumerable<string>? tags, int maximum = MaximumScopeTags)
    {
        if (tags is null)
        {
            return [];
        }

        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var tag in tags)
        {
            var value = Normalize(tag);
            if (!seen.Add(value))
            {
                throw new ArgumentException($"Knowledge tag '{value}' is duplicated.", nameof(tags));
            }

            normalized.Add(value);
            if (normalized.Count > maximum)
            {
                throw new ArgumentException($"A Knowledge scope cannot contain more than {maximum} tags.", nameof(tags));
            }
        }

        return normalized;
    }

    /// <summary>Validates an optional narrowing and returns the effective immutable scope.</summary>
    public static IReadOnlyList<string> Narrow(
        IEnumerable<string> owningTags,
        IEnumerable<string>? requestedTags,
        int requestedMaximum = MaximumScopeTags)
    {
        var owner = NormalizeScope(owningTags).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requestedTags is null)
        {
            return owner.Order(StringComparer.Ordinal).ToArray();
        }

        var requested = NormalizeScope(requestedTags, requestedMaximum);
        foreach (var tag in requested)
        {
            if (!owner.Contains(tag))
            {
                throw new KnowledgeAuthorizationException("The requested Knowledge tag is not authorized by the owning scope.");
            }
        }

        return requested.ToArray();
    }
}
