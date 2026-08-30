using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies bundled editorial workflows fail closed when discovery has too few candidates.
/// </summary>
public sealed class EditorialWorkflowSampleTests
{
    /// <summary>
    /// Verifies an empty daily selection cannot be converted into invented LinkedIn posts.
    /// </summary>
    [Fact]
    public void DailyLinkedInSampleStopsAfterCollectionDiagnosticWhenEmpty()
    {
        using var document = LoadSample("editorial-watch-daily.json");
        var workflow = document.RootElement.GetProperty("workflow");
        var discovery = workflow[1].GetProperty("args");
        var promptStep = workflow[2];
        var prompt = promptStep.GetProperty("args").GetProperty("prompt").GetString();

        workflow[0].GetProperty("step").GetString().Should().Be("sqlite.createProposalDatabase");
        discovery.GetProperty("freshnessWindow").GetString().Should().Be("sliding");
        discovery.GetProperty("previousProposalsLookbackMonths").GetInt32().Should().Be(6);
        discovery.GetProperty("usePreviousProposalUsedTag").GetBoolean().Should().BeFalse();
        promptStep.GetProperty("toolAliases").EnumerateArray()
            .Select(static value => value.GetString())
            .Should().Equal("sqlite.storeProposal");
        document.RootElement.GetProperty("tools").EnumerateArray()
            .Select(static value => value.GetString())
            .Should().Contain("sqlite");
        document.RootElement.GetProperty("capabilities").EnumerateArray()
            .Select(static value => value.GetString())
            .Should().Contain("SqliteWrite");
        prompt.Should().Contain("Si searchStatus vaut rateLimited, unavailable ou degraded")
            .And.Contain("Si searchStatus vaut available et candidateCount vaut 0")
            .And.Contain("n'écris rien dans SQLite")
            .And.Contain("appelle sqlite.storeProposal exactement une fois")
            .And.Contain("Ne fournis ni SQL, ni vecteur, ni date de création");
    }

    /// <summary>
    /// Verifies the Sunday sample writes from one source without inventing multi-source evidence.
    /// </summary>
    [Fact]
    public void SundayArticleSampleAllowsOneFocusedSource()
    {
        using var document = LoadSample("editorial-blog-sunday.json");
        var workflow = document.RootElement.GetProperty("workflow");
        var discovery = workflow[0].GetProperty("args");
        var prompt = workflow[1].GetProperty("args").GetProperty("prompt").GetString();

        discovery.GetProperty("freshnessWindow").GetString().Should().Be("sliding");
        prompt.Should().Contain("Si searchStatus vaut rateLimited, unavailable ou degraded")
            .And.Contain("recherche Web indisponible")
            .And.Contain("Si searchStatus vaut available et candidateCount vaut 0")
            .And.Contain("Si candidateCount vaut 1")
            .And.Contain("rédige un article focalisé sur cette source unique")
            .And.Contain("Ne prétends pas établir une tendance")
            .And.Contain("entre 1 et 8")
            .And.NotContain("Si candidateCount est inférieur à 3");
    }

    private static JsonDocument LoadSample(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OD.MyLocalAgent.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        var path = Path.Combine(directory!.FullName, "Docs", "samples", fileName);
        return JsonDocument.Parse(File.ReadAllText(path));
    }
}
