using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies deterministic prompt-only placeholder expansion and preservation.
/// </summary>
public sealed class AgentPromptPlaceholderResolverTests
{
    private static readonly DateTimeOffset LocalNow = new(
        2026,
        8,
        3,
        14,
        30,
        25,
        TimeSpan.FromHours(2));

    /// <summary>
    /// Verifies default and custom date/time values use one explicit local snapshot.
    /// </summary>
    [Fact]
    public void ResolvesDateTimeDefaultsAndCustomFormats()
    {
        const string prompt =
            "{{Today}}|{{Now}}|{{Today[yyyyMMdd]}}|{{Today[yyyy-MM-dd]}}|{{Now[HHmmss]}}|{{Now[yyyyMMdd'-report-'HHmmss]}}";

        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "dated-task",
            "Dated task",
            LocalNow);

        result.ResolvedPrompt.Should().Be(
            "2026-08-03|2026-08-03T14:30:25+02:00|20260803|2026-08-03|143025|20260803-report-143025");
        result.UnresolvedOccurrences.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies filename-friendly aliases preserve their established case-sensitive meaning.
    /// </summary>
    [Fact]
    public void ResolvesFriendlyAliases()
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            "{{Today[YYYY-YY-DD]}} {{Now[hhmmss]}}",
            "aliases",
            "Aliases",
            LocalNow);

        result.ResolvedPrompt.Should().Be("2026-26-03 143025");
    }

    /// <summary>
    /// Verifies names are case-insensitive and surrounding marker whitespace is accepted.
    /// </summary>
    [Fact]
    public void ResolvesCaseInsensitiveNamesWithWhitespace()
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            "{{  tOdAy[yyyyMMdd]  }} {{ TASK.ID }} {{ task.NAME }}",
            "identity",
            "Résumé 日本語",
            LocalNow);

        result.ResolvedPrompt.Should().Be(
            "20260803 identity Résumé 日本語");
        result.TotalUnresolvedCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies every occurrence receives the same supplied time value.
    /// </summary>
    [Fact]
    public void UsesOneSuppliedSnapshotForEveryOccurrence()
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            "{{Now[O]}}|{{Today}}|{{Now[O]}}|{{Today}}",
            "snapshot",
            "Snapshot",
            LocalNow);

        result.ResolvedPrompt.Should().Be(
            "2026-08-03T14:30:25.0000000+02:00|2026-08-03|2026-08-03T14:30:25.0000000+02:00|2026-08-03");
    }

    /// <summary>
    /// Verifies task identity is inserted exactly and replacement text is not rescanned.
    /// </summary>
    [Fact]
    public void InsertsIdentityExactlyWithoutRecursiveResolution()
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            "Run {{task.name}} as {{task.id}}.",
            "archive-task",
            "Archive {{Today}} and {{customer_name}}",
            LocalNow);

        result.ResolvedPrompt.Should().Be(
            "Run Archive {{Today}} and {{customer_name}} as archive-task.");
        result.TotalUnresolvedCount.Should().Be(0);
    }

    /// <summary>
    /// Verifies unknown markers remain character-for-character identical.
    /// </summary>
    [Fact]
    public void PreservesUnknownMarkerExactly()
    {
        const string prompt = "Write for {{  Customer_Name  }} now.";

        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "unknown",
            "Unknown",
            LocalNow);

        result.ResolvedPrompt.Should().BeSameAs(prompt);
        result.UnresolvedOccurrences.Should().ContainSingle()
            .Which.Should().Be(
                new AgentPromptPlaceholderOccurrence(
                    AgentPromptPlaceholderWarningKind.Unknown,
                    10,
                    21));
    }

    /// <summary>
    /// Verifies every defined workflow-only marker class remains unavailable.
    /// </summary>
    [Theory]
    [InlineData("{{task.prompt}}")]
    [InlineData("{{previous.output}}")]
    [InlineData("{{previous.output.items[0].name}}")]
    [InlineData("{{workflow.context}}")]
    [InlineData("{{step-one.output}}")]
    [InlineData("{{gmail.readToday.output.messages[0].content}}")]
    public void ClassifiesWorkflowOnlyMarkers(string prompt)
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "workflow-only",
            "Workflow only",
            LocalNow);

        result.ResolvedPrompt.Should().Be(prompt);
        result.UnresolvedOccurrences.Should().ContainSingle()
            .Which.Kind.Should().Be(
                AgentPromptPlaceholderWarningKind.WorkflowOnly);
    }

    /// <summary>
    /// Verifies invalid date/time formats remain exact and are classified consistently.
    /// </summary>
    [Theory]
    [InlineData("{{Today[]}}")]
    [InlineData("{{Now[   ]}}")]
    [InlineData("{{Today[yyyy[MM]]}}")]
    [InlineData("{{Now[yyyy-MM-dd]extra}}")]
    [InlineData("{{Today[yyyy-MM-dd 'unterminated]}}")]
    public void PreservesInvalidDateTimeFormats(string prompt)
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "invalid-date",
            "Invalid date",
            LocalNow);

        result.ResolvedPrompt.Should().Be(prompt);
        result.UnresolvedOccurrences.Should().ContainSingle()
            .Which.Kind.Should().Be(
                AgentPromptPlaceholderWarningKind.InvalidDateTimeFormat);
    }

    /// <summary>
    /// Verifies an excessive date/time format is preserved and warned.
    /// </summary>
    [Fact]
    public void PreservesExcessiveDateTimeFormat()
    {
        var prompt = "{{Today[" + new string('y', 101) + "]}}";

        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "long-date",
            "Long date",
            LocalNow);

        result.ResolvedPrompt.Should().Be(prompt);
        result.GetCount(AgentPromptPlaceholderWarningKind.InvalidDateTimeFormat)
            .Should().Be(1);
    }

    /// <summary>
    /// Verifies malformed double braces are preserved and scanned in bounded source order.
    /// </summary>
    [Theory]
    [InlineData("{{}}")]
    [InlineData("{{   }}")]
    [InlineData("{{outer {{inner}}}}")]
    [InlineData("before {{unmatched")]
    [InlineData("before }} after")]
    [InlineData("{{{Today}}}")]
    public void PreservesMalformedBraces(string prompt)
    {
        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "braces",
            "Braces",
            LocalNow);

        result.ResolvedPrompt.Should().Be(prompt);
        result.UnresolvedOccurrences.Should().NotBeEmpty();
        result.UnresolvedOccurrences.Should().OnlyContain(
            occurrence =>
                occurrence.Kind == AgentPromptPlaceholderWarningKind.MalformedBraces);
    }

    /// <summary>
    /// Verifies adjacent repeated warnings retain exact counts and first-source ordering.
    /// </summary>
    [Fact]
    public void CountsRepeatedMarkersInSourceOrder()
    {
        const string prompt =
            "{{unknown}}{{previous.output}}{{unknown}}{{Today[]}}}}";

        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "counts",
            "Counts",
            LocalNow);

        result.UnresolvedOccurrences.Select(static occurrence => occurrence.Kind)
            .Should().Equal(
                AgentPromptPlaceholderWarningKind.Unknown,
                AgentPromptPlaceholderWarningKind.WorkflowOnly,
                AgentPromptPlaceholderWarningKind.Unknown,
                AgentPromptPlaceholderWarningKind.InvalidDateTimeFormat,
                AgentPromptPlaceholderWarningKind.MalformedBraces);
        result.TotalUnresolvedCount.Should().Be(5);
        result.GetCount(AgentPromptPlaceholderWarningKind.Unknown).Should().Be(2);
    }

    /// <summary>
    /// Verifies prompts without markers retain the original string instance and all characters.
    /// </summary>
    [Fact]
    public void LeavesMarkerFreeUnicodeAndLineEndingsUntouched()
    {
        const string prompt = "JSON { value }\r\nEmoji 😀\nAccents éà 日本語";

        var result = AgentPromptPlaceholderResolver.Resolve(
            prompt,
            "plain",
            "Plain",
            LocalNow);

        result.ResolvedPrompt.Should().BeSameAs(prompt);
        result.UnresolvedOccurrences.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies content-free summaries expose counts and categories but no source values.
    /// </summary>
    [Fact]
    public void WarningSummaryContainsOnlyProgramOwnedCountsAndCategories()
    {
        const string canary = "{{private_customer_canary}}";
        var result = AgentPromptPlaceholderResolver.Resolve(
            canary + " {{previous.output}}",
            "private-id",
            "Private task name",
            LocalNow);

        var warning = AgentPromptPlaceholderWarning.FormatSummary(result);

        warning.Should().Be(
            "Warning: 2 agent prompt placeholder occurrences were left unchanged (1 unknown, 1 workflow-only).");
        warning.Should().NotContain(canary);
        warning.Should().NotContain("Private task name");
        warning.Should().NotContain("private-id");
    }

    /// <summary>
    /// Verifies independent concurrent calls cannot observe another task's values.
    /// </summary>
    [Fact]
    public async Task ConcurrentResolutionsRemainIsolated()
    {
        var firstNow = LocalNow;
        var secondNow = LocalNow.AddDays(1).AddHours(2);

        var results = await Task.WhenAll(
            Task.Run(
                () => AgentPromptPlaceholderResolver.Resolve(
                    "{{task.id}}|{{task.name}}|{{Now[O]}}",
                    "first",
                    "First",
                    firstNow)),
            Task.Run(
                () => AgentPromptPlaceholderResolver.Resolve(
                    "{{task.id}}|{{task.name}}|{{Now[O]}}",
                    "second",
                    "Second",
                    secondNow)));

        results[0].ResolvedPrompt.Should().StartWith("first|First|2026-08-03");
        results[1].ResolvedPrompt.Should().StartWith("second|Second|2026-08-04");
    }
}
