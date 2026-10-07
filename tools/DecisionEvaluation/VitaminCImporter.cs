using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace OD.MyLocalAgent.Evaluation;

/// <summary>Imports a pinned, label-blind family subset without executing upstream code or altering annotations.</summary>
public static class VitaminCImporter
{
    /// <summary>The pinned benchmark recipe, recorded separately from the upstream dataset's file hash.</summary>
    public const string RecipeRevision = "dcfdbd9a64f0d869f658d7a72f1beaee32737773";
    /// <summary>The fixed source question from the public benchmark converter.</summary>
    public const string Question = "Decide how the evidence bears on the claim. Judge only from the evidence text, and not from outside knowledge about the subject.";

    /// <summary>Converts selected whole families from a local dev JSONL file and verifies the pinned source checksum.</summary>
    public static void Import(string sourcePath, string manifestPath, string directory, int limit)
    {
        if (limit is < 4 or > 599) throw new ArgumentException("Public pilot limit must be between 4 and 599.");
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new IOException("Use an empty import directory.");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var metadata = manifest.RootElement;
        if (metadata.GetProperty("dataset").GetString() != "vitaminc" || metadata.GetProperty("license").GetString() != "CC BY-SA 3.0")
            throw new InvalidDataException("Expected the VitaminC CC BY-SA 3.0 benchmark manifest.");
        using var stream = File.OpenRead(sourcePath);
        var sourceHash = Convert.ToHexString(SHA256.HashData(stream));
        if (!sourceHash.Equals(metadata.GetProperty("source_sha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Source checksum does not match the pinned public manifest.");
        stream.Position = 0;
        var ids = metadata.GetProperty("ids").EnumerateArray().Select(i => i.GetString()!).ToArray();
        var selectedIds = ids.ToHashSet(StringComparer.Ordinal);
        var available = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            using var row = JsonDocument.Parse(line);
            var id = "vitaminc-" + row.RootElement.GetProperty("unique_id").GetString();
            if (selectedIds.Contains(id) && !available.TryAdd(id, row.RootElement.Clone())) throw new InvalidDataException("Duplicate source id.");
        }
        if (available.Count != ids.Length) throw new InvalidDataException("Some pinned public ids are missing.");
        var families = ids.Select(id => available[id]).GroupBy(r => r.GetProperty("case_id").GetString()!).ToArray();
        var selected = new List<JsonElement>();
        foreach (var family in families)
        {
            if (selected.Count + family.Count() > limit) break;
            selected.AddRange(family);
        }
        if (selected.Count == 0) throw new InvalidDataException("No complete family fits the limit.");
        var options = new Dictionary<string, string>
        {
            ["SUPPORTS"] = "The evidence states the claim, or the claim follows directly from the evidence.",
            ["REFUTES"] = "The evidence states the opposite of the claim, or the claim is directly contradicted by it.",
            ["NOT_ENOUGH_INFO"] = "The evidence neither establishes nor contradicts the claim. Settling it would need a fact the evidence does not supply.",
        };
        var cases = selected.Select(row => new
        {
            id = "vitaminc-" + row.GetProperty("unique_id").GetString(), pairId = row.GetProperty("case_id").GetString(),
            language = "en", split = "public", category = "public-benchmark", question = Question, options,
            source = JsonSerializer.Serialize(new { evidence = row.GetProperty("evidence").GetString(), claim = row.GetProperty("claim").GetString() }),
            expected = row.GetProperty("label").GetString()!.Replace(' ', '_'),
        }).ToArray();
        Directory.CreateDirectory(directory);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true };
        File.WriteAllText(Path.Combine(directory, "corpus.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1, fixtureVersion = "vitaminc-application-pilot-v1", sourceHash, recipeRevision = RecipeRevision,
            license = "CC BY-SA 3.0", attribution = "Tal Schuster et al., VitaminC (2021); public benchmark recipe by Bespoke Labs.",
            sourceUrl = "https://huggingface.co/datasets/tals/vitaminc", benchmarkUrl = "https://github.com/bespokelabsai/nimble/blob/" + RecipeRevision + "/docs/PUBLIC_BENCHMARKS.md",
            adaptation = "State object serialized as literal source text; NOT ENOUGH INFO renamed to NOT_ENOUGH_INFO for the application identifier contract. No truncation. Not an exact replication of published benchmark accuracy.",
            selection = "First complete families in the pinned manifest order, without consulting labels; smaller public pilot, not the full suite.",
            cases,
        }, json));
        File.Copy(manifestPath, Path.Combine(directory, "upstream-manifest.json"));
        File.WriteAllText(Path.Combine(directory, "ATTRIBUTION.md"), "# VitaminC pilot inputs\n\nVitaminC by Tal Schuster et al. (2021), licensed CC BY-SA 3.0: https://huggingface.co/datasets/tals/vitaminc\n\nLicense: https://creativecommons.org/licenses/by-sa/3.0/\n\nBespoke Labs benchmark recipe: https://github.com/bespokelabsai/nimble/tree/" + RecipeRevision + "\n\nAdaptations: literal JSON source representation, normalized NOT_ENOUGH_INFO identifier, smaller whole-family subset. These adapted inputs remain under CC BY-SA 3.0; do not remove attribution when sharing. Raw inputs remain local and are not committed to the application repository.\n");
    }

    /// <summary>Copies only the dev JSONL entry from a local archive, avoiding filesystem extraction of untrusted paths.</summary>
    public static void ExtractDevelopment(string archivePath, string outputPath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.Entries.Single(e => e.FullName.Replace('\\', '/').EndsWith("/dev.jsonl", StringComparison.Ordinal) || e.FullName == "dev.jsonl");
        if (entry.Length > 256 * 1024 * 1024) throw new InvalidDataException("Unexpectedly large public dataset entry.");
        using var source = entry.Open();
        using var destination = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write);
        source.CopyTo(destination);
    }
}
