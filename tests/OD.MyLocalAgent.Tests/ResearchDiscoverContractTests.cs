using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies query compilation, URL canonicalization, and research.discover contracts.
/// </summary>
public sealed class ResearchDiscoverContractTests
{
    /// <summary>
    /// Verifies single theme produces one primary query.
    /// </summary>
    [Fact]
    public void CompileSingleThemeProducesOnePrimaryQuery()
    {
        var themes = new[]
        {
            new Theme([".NET", "C#", "ASP.NET Core"], ["release", "preview"], ["job", "course"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            10);

        queries.Should().ContainSingle();
        queries[0].Intent.Should().Be(QueryIntent.Primary);
        queries[0].ThemeIndex.Should().Be(0);
        queries[0].Query.Should().Contain(".NET");
        queries[0].Query.Should().Contain("C#");
        queries[0].Query.Should().Contain("ASP.NET Core");
        queries[0].Query.Should().Contain("current change");
        queries[0].Query.Should().Contain("release");
        queries[0].Query.Should().Contain("-job");
        queries[0].Query.Should().Contain("-course");
        queries[0].Query.Should().Contain("lang:en");
    }

    /// <summary>
    /// Verifies two themes produce one primary query each.
    /// </summary>
    [Fact]
    public void CompileTwoThemesProducesTwoQueries()
    {
        var themes = new[]
        {
            new Theme(["Rust"]),
            new Theme(["Go", "Golang"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            10);

        queries.Should().HaveCount(2);
        queries[0].ThemeIndex.Should().Be(0);
        queries[1].ThemeIndex.Should().Be(1);
    }

    /// <summary>
    /// Verifies fallback compilation produces one practical-engineering query.
    /// </summary>
    [Fact]
    public void CompileFallbackProducesPracticalEngineeringQuery()
    {
        var themes = new[]
        {
            new Theme([".NET"]),
        };

        var query = QueryCompiler.CompileFallback(themes[0], 0, "en", null);

        query.Intent.Should().Be(QueryIntent.Fallback);
        query.ThemeIndex.Should().Be(0);
        query.Query.Should().Contain("practical engineering");
    }

    /// <summary>
    /// Verifies budget limits the number of generated queries.
    /// </summary>
    [Fact]
    public void CompileRespectsBudgetLimit()
    {
        var themes = new[]
        {
            new Theme([".NET"]),
            new Theme(["Rust"]),
            new Theme(["Go"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            2);

        queries.Should().HaveCount(2);
    }

    /// <summary>
    /// Verifies sliding mode adds date qualifier.
    /// </summary>
    [Fact]
    public void CompileSlidingModeAddsDateQualifier()
    {
        var themes = new[]
        {
            new Theme([".NET"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            new DateOnly(2026, 8, 14),
            10);

        queries.Should().ContainSingle();
        queries[0].Query.Should().Contain("after:2026-08-14");
    }

    /// <summary>
    /// Verifies none mode has no date qualifier.
    /// </summary>
    [Fact]
    public void CompileNoneModeHasNoDateQualifier()
    {
        var themes = new[]
        {
            new Theme([".NET"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            10);

        queries.Should().ContainSingle();
        queries[0].Query.Should().NotContain("after:");
    }

    /// <summary>
    /// Verifies URL canonicalization removes fragment identifiers.
    /// </summary>
    [Fact]
    public void CanonicalizeRemovesFragments()
    {
        var url = "https://example.com/article#section1";
        var canonical = UrlCanonicalizer.Canonicalize(url);
        canonical.Should().Be("https://example.com/article");
    }

    /// <summary>
    /// Verifies URL canonicalization removes tracking parameters.
    /// </summary>
    [Fact]
    public void CanonicalizeRemovesTrackingParameters()
    {
        var url = "https://example.com/article?utm_source=test&utm_medium=email&page=1";
        var canonical = UrlCanonicalizer.Canonicalize(url);
        canonical.Should().Be("https://example.com/article?page=1");
    }

    /// <summary>
    /// Verifies URL canonicalization normalizes host casing.
    /// </summary>
    [Fact]
    public void CanonicalizeNormalizesHostCasing()
    {
        var url = "https://EXAMPLE.COM/Article";
        var canonical = UrlCanonicalizer.Canonicalize(url);
        canonical.Should().Be("https://example.com/Article");
    }

    /// <summary>
    /// Verifies URL canonicalization removes default ports.
    /// </summary>
    [Fact]
    public void CanonicalizeRemovesDefaultPorts()
    {
        var url = "https://example.com:443/article";
        var canonical = UrlCanonicalizer.Canonicalize(url);
        canonical.Should().Be("https://example.com/article");
    }

    /// <summary>
    /// Verifies URL canonicalization preserves non-default ports.
    /// </summary>
    [Fact]
    public void CanonicalizePreservesNonDefaultPorts()
    {
        var url = "https://example.com:8080/article";
        var canonical = UrlCanonicalizer.Canonicalize(url);
        canonical.Should().Be("https://example.com:8080/article");
    }

    /// <summary>
    /// Verifies domain extraction works correctly.
    /// </summary>
    [Fact]
    public void ExtractDomainReturnsDomain()
    {
        var url = "https://example.com/article?query=test";
        var domain = UrlCanonicalizer.ExtractDomain(url);
        domain.Should().Be("example.com");
    }

    /// <summary>
    /// Verifies empty themes produce no queries.
    /// </summary>
    [Fact]
    public void CompileEmptyThemesProducesNoQueries()
    {
        var queries = QueryCompiler.Compile(
            [],
            "en",
            null,
            10);

        queries.Should().BeEmpty();
    }

    /// <summary>
    /// Verifies theme without include/exclude terms works correctly.
    /// </summary>
    [Fact]
    public void CompileThemeWithoutFiltersWorks()
    {
        var themes = new[]
        {
            new Theme(["Rust", "Cargo"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            10);

        queries.Should().ContainSingle();
        queries[0].Query.Should().Contain("Rust");
        queries[0].Query.Should().Contain("Cargo");
        queries[0].Query.Should().NotContain("-");
    }

    /// <summary>
    /// Verifies include terms are added to the query.
    /// </summary>
    [Fact]
    public void CompileIncludeTermsAreAddedToQuery()
    {
        var themes = new[]
        {
            new Theme([".NET"], ["release", "preview"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            10);

        queries.Should().ContainSingle();
        queries[0].Query.Should().Contain("release");
        queries[0].Query.Should().Contain("preview");
    }

    /// <summary>
    /// Verifies exclude terms are added with minus prefix.
    /// </summary>
    [Fact]
    public void CompileExcludeTermsAreAddedWithMinusPrefix()
    {
        var themes = new[]
        {
            new Theme([".NET"], null, ["job", "course"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "en",
            null,
            10);

        queries.Should().ContainSingle();
        queries[0].Query.Should().Contain("-job");
        queries[0].Query.Should().Contain("-course");
    }

    /// <summary>
    /// Verifies source language qualifier is added.
    /// </summary>
    [Fact]
    public void CompileAddsSourceLanguageQualifier()
    {
        var themes = new[]
        {
            new Theme([".NET"]),
        };

        var queries = QueryCompiler.Compile(
            themes,
            "fr",
            null,
            10);

        queries.Should().ContainSingle();
        queries[0].Query.Should().Contain("lang:fr");
    }

    /// <summary>
    /// Verifies research.discover tool is registered in the catalog.
    /// </summary>
    [Fact]
    public void ResearchDiscoverToolIsRegistered()
    {
        var definition = ToolMap.Find("research.discover");
        definition.Should().NotBeNull();
        definition!.ToolGroup.Should().Be("research");
        definition.IsAgentTool.Should().BeTrue();
        definition.Arguments.Should().Contain("themes");
        definition.Arguments.Should().Contain("freshnessWindow");
        definition.OptionalArguments.Should().Contain("sourceLanguage");
        definition.OptionalArguments.Should().Contain("language");
        definition.OptionalArguments.Should().Contain("preferredDomains");
        definition.OptionalArguments.Should().Contain("minimumDocuments");
        definition.OptionalArguments.Should().Contain("maxResultsPerQuery");
        definition.OptionalArguments.Should().Contain("maxRetrievalsPerRun");
    }

    /// <summary>
    /// Verifies research group is in the task whitelist.
    /// </summary>
    [Fact]
    public void ResearchGroupIsInTaskWhitelist()
    {
        ToolGroups.All.Should().Contain("research");
    }

    /// <summary>
    /// Verifies research group has correct display order.
    /// </summary>
    [Fact]
    public void ResearchGroupHasCorrectDisplayOrder()
    {
        var group = ToolGroups.Find("research");
        group.Should().NotBeNull();
        group!.DisplayOrder.Should().Be(1200);
    }

    /// <summary>
    /// Verifies ToolMap shows 8 arguments total for research.discover (1 required + 7 optional).
    /// </summary>
    [Fact]
    public void ResearchDiscoverToolHasCorrectArgumentCount()
    {
        var definition = ToolMap.Find("research.discover");
        definition.Should().NotBeNull();
        definition!.Arguments.Count.Should().Be(8);
        definition.OptionalArguments.Count.Should().Be(7);
    }

    /// <summary>
    /// Verifies research.discover is an agent tool (not task whitelist).
    /// </summary>
    [Fact]
    public void ResearchDiscoverIsAgentTool()
    {
        var definition = ToolMap.Find("research.discover");
        definition.Should().NotBeNull();
        definition!.IsAgentTool.Should().BeTrue();
    }

    /// <summary>
    /// Verifies ToolCatalog knows about the research group.
    /// </summary>
    [Fact]
    public void ToolCatalogContainsResearchGroup()
    {
        var catalog = ToolCatalog.FromAssembly(typeof(ToolGroups).Assembly);
        var researchGroup = catalog.Groups.FirstOrDefault(g => g.Name == "research");
        researchGroup.Should().NotBeNull();
        researchGroup!.DisplayOrder.Should().Be(1200);
    }
}
