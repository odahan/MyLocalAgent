using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>
/// Verifies bundled editorial workflows preserve valid sparse discoveries without inventing subjects.
/// </summary>
public sealed class EditorialWorkflowSampleTests
{
    /// <summary>
    /// Verifies an empty daily selection cannot be converted into invented LinkedIn posts.
    /// </summary>
    [Fact]
    public void DailyLinkedInSampleRequiresStructuredReservationAndConfirmedExport()
    {
        using var document = LoadSample("editorial-watch-daily-db.json");
        var workflow = document.RootElement.GetProperty("workflow");
        var discovery = workflow[1].GetProperty("args");
        var discoveryRoute = workflow[2];
        var emptyReservation = workflow[3];
        var selectionStep = workflow[4];
        var selectionPrompt = selectionStep.GetProperty("args").GetProperty("prompt").GetString();

        document.RootElement.GetProperty("id").GetString().Should().Be("editorial-watch-daily-db");
        workflow[0].GetProperty("step").GetString().Should().Be("sqlite.createProposalDatabase");
        discovery.GetProperty("freshnessWindow").GetString().Should().Be("sliding");
        discovery.GetProperty("previousProposalsLookbackMonths").GetInt32().Should().Be(6);
        discovery.GetProperty("usePreviousProposalUsedTag").GetBoolean().Should().BeFalse();
        discoveryRoute.GetProperty("step").GetString().Should().Be("flow.decision");
        discoveryRoute.GetProperty("args").GetProperty("path").GetString().Should().Be("candidateCount");
        discoveryRoute.GetProperty("args").GetProperty("target").GetString().Should().Be("selectEditorialSubjects");
        discoveryRoute.GetProperty("next").GetString().Should().Be("reserveEmptyDiscovery");
        emptyReservation.GetProperty("step").GetString().Should().Be("sqlite.reserveEditorialSelection");
        emptyReservation.GetProperty("args").GetProperty("selectionJson").GetString().Should().Be("{\"proposals\":[]}");
        emptyReservation.GetProperty("next").GetString().Should().Be("prepareEmptyReport");
        var proposalsSchema = selectionStep.GetProperty("responseSchema").GetProperty("properties").GetProperty("proposals");
        proposalsSchema.GetProperty("minItems").GetInt32().Should().Be(1);
        proposalsSchema.GetProperty("maxItems").GetInt32().Should().Be(3);
        var proposalProperties = proposalsSchema.GetProperty("items").GetProperty("properties");
        proposalProperties.GetProperty("sourceIds").GetProperty("minItems").GetInt32().Should().Be(1);
        var claimedFacts = proposalProperties.GetProperty("claimedNewFacts");
        claimedFacts.GetProperty("minItems").GetInt32().Should().Be(1);
        claimedFacts.GetProperty("items").GetProperty("properties").GetProperty("sourceIds")
            .GetProperty("minItems").GetInt32().Should().Be(1);
        selectionStep.GetProperty("onError").GetString().Should().Be("Retry");
        selectionStep.GetProperty("retryCount").GetInt32().Should().Be(2);
        selectionStep.TryGetProperty("fallbackOutput", out _).Should().BeFalse();
        selectionStep.TryGetProperty("toolAliases", out _).Should().BeFalse();
        workflow[5].GetProperty("step").GetString().Should().Be("sqlite.reserveEditorialSelection");
        workflow[6].GetProperty("step").GetString().Should().Be("flow.decision");
        workflow[6].GetProperty("args").GetProperty("path").GetString().Should().Be("approvedCount");
        workflow[6].GetProperty("args").GetProperty("target").GetString().Should().Be("draftApprovedSubjects");
        workflow[6].GetProperty("next").GetString().Should().Be("prepareEmptyReport");
        workflow[7].GetProperty("step").GetString().Should().Be("sqlite.prepareEditorialReport");
        workflow[7].GetProperty("args").GetProperty("draftsJson").GetString().Should().Be("{\"subjects\":[]}");
        workflow[8].GetProperty("args").GetProperty("mode").GetString().Should().Be("createNew");
        workflow[8].GetProperty("args").GetProperty("path").GetString().Should().Contain("{{workflow.runId}}");
        workflow[9].GetProperty("step").GetString().Should().Be("sqlite.confirmEditorialExport");
        workflow[10].GetProperty("responseSchema").GetProperty("properties").GetProperty("subjects")
            .GetProperty("items").GetProperty("properties").GetProperty("criticalAngle")
            .GetProperty("maxLength").GetInt32().Should().Be(1600);
        workflow[12].GetProperty("args").GetProperty("mode").GetString().Should().Be("createNew");
        workflow[13].GetProperty("step").GetString().Should().Be("sqlite.confirmEditorialExport");
        document.RootElement.GetProperty("tools").EnumerateArray()
            .Select(static value => value.GetString())
            .Should().Contain("sqlite");
        document.RootElement.GetProperty("capabilities").EnumerateArray()
            .Select(static value => value.GetString())
            .Should().Contain("SqliteWrite");
        selectionPrompt.Should().Contain("entre un et trois sujets")
            .And.Contain("au moins une proposition")
            .And.Contain("ne sont pas des motifs pour retourner proposals vide")
            .And.Contain("sourceIds présents")
            .And.Contain("Uncertain")
            .And.Contain("dernier contrôle anti-répétition");

        var task = ScheduledTaskSerializer.Deserialize(File.ReadAllText(SamplePath("editorial-watch-daily-db.json")));
        var validation = () => ScheduledTaskValidator.Validate(task);
        validation.Should().NotThrow();
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
        => JsonDocument.Parse(File.ReadAllText(SamplePath(fileName)));

    private static string SamplePath(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "OD.MyLocalAgent.sln")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run below the repository root");
        return Path.Combine(directory!.FullName, "Docs", "samples", fileName);
    }
}
