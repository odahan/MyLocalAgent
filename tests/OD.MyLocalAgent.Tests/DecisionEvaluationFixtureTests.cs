using System.Diagnostics;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using OD.MyLocalAgent.Core;
using Xunit;

namespace OD.MyLocalAgent.Tests;

/// <summary>Validates synthetic paired fixtures and the offline preparation script without model calls.</summary>
public sealed class DecisionEvaluationFixtureTests
{
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "OD.MyLocalAgent.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    /// <summary>Every paired source fits the actual contract and budget with a frozen held-out partition.</summary>
    [Fact]
    public void PairedCorpusHasValidBoundedContractsAndReservedCases()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(RepositoryRoot(), "Docs", "evaluation", "decision-readiness-pairs.v1.json")));
        var corpus = document.RootElement;
        corpus.GetProperty("labelReviewStatus").GetString().Should().Contain("review required");
        var pairs = corpus.GetProperty("pairs").EnumerateArray().ToArray();
        pairs.Should().HaveCount(30);
        pairs.Select(p => p.GetProperty("id").GetString()).Should().OnlyHaveUniqueItems();
        pairs.Count(p => p.GetProperty("split").GetString() == "held-out").Should().Be(10);
        var question = corpus.GetProperty("question").GetString()!;
        var options = corpus.GetProperty("options");
        foreach (var pair in pairs)
        {
            options.TryGetProperty(pair.GetProperty("expected").GetString()!, out _).Should().BeTrue();
            pair.GetProperty("rationale").GetString().Should().NotBeNullOrWhiteSpace();
            foreach (var language in new[] { "fr", "en" })
            {
                var source = pair.GetProperty(language).GetString()!;
                DecisionEvaluateContract.Validate(JsonSerializer.SerializeToElement(new { source, question, options }));
                var questions = DecisionEvaluateContract.Questions(question, options);
                (Encoding.UTF8.GetByteCount(source) + Encoding.UTF8.GetByteCount(questions.Json.GetRawText()) + 400)
                    .Should().BeLessThanOrEqualTo(new DecisionOptions().MaxInputTokens, pair.GetProperty("id").GetString() + "-" + language);
            }
        }
    }

    /// <summary>The shipped script generates sixty disabled valid tasks and a reproducible manifest offline.</summary>
    [Fact]
    public async Task OfflinePreparationProducesOnlyDisabledDecisionTasks()
    {
        var root = RepositoryRoot();
        var output = Path.Combine(Path.GetTempPath(), "decision-fixtures-" + Guid.NewGuid().ToString("N"));
        try
        {
            var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File",
                Path.Combine(root, "tools", "Prepare-DecisionEvaluation.ps1"), "-OutputDirectory", output }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); throw; }
            process.ExitCode.Should().Be(0, await stderr);
            (await stdout).Should().Contain("No model execution");
            var inputs = Directory.GetFiles(output, "p*.json");
            inputs.Should().HaveCount(60);
            foreach (var input in inputs)
            {
                var task = ScheduledTaskSerializer.Deserialize(File.ReadAllText(input));
                task.Enabled.Should().BeFalse();
                task.Tools.Should().Equal("decision");
                task.Workflow.Should().ContainSingle();
                task.Workflow[0].Step.Should().Be("decision.evaluate");
                task.Workflow[0].Next.Should().Be("@end");
            }
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "manifest.json")));
            manifest.RootElement.GetProperty("executionPerformed").GetBoolean().Should().BeFalse();
            manifest.RootElement.GetProperty("fixtureSha256").GetString().Should().HaveLength(64);
        }
        finally { if (Directory.Exists(output)) Directory.Delete(output, recursive: true); }
    }
}
