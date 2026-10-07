using System.Net.Http.Json;
using System.Text.Json;
using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Evaluation;

/// <summary>Resolves the installed general comparator without conflating models served by different runners.</summary>
public static class GeneralModelIdentity
{
    /// <summary>Resolves duplicate model tags by the runner exposed by the server, rejecting ambiguous evidence.</summary>
    public static async Task<(string Digest, object Metadata)> ResolveAsync(HttpClient http, OllamaOptions local, CancellationToken token)
    {
        using var response = await http.PostAsJsonAsync(new Uri(new Uri(local.Endpoint), "/api/show"), new { model = local.Model }, token);
        response.EnsureSuccessStatusCode();
        using var metadata = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var details = metadata.RootElement.GetProperty("details");
        var runner = details.TryGetProperty("runner", out var declaredRunner) ? declaredRunner.GetString() : null;
        var tags = await http.GetFromJsonAsync<JsonElement>(new Uri(new Uri(local.Endpoint), "/api/tags"), token);
        var candidates = tags.GetProperty("models").EnumerateArray()
            .Where(model => string.Equals(model.GetProperty("name").GetString(), local.Model, StringComparison.OrdinalIgnoreCase));
        if (runner is not null)
            candidates = candidates.Where(model => model.GetProperty("details").TryGetProperty("runner", out var candidateRunner) && candidateRunner.GetString() == runner);
        var digests = candidates.Select(model => model.GetProperty("digest").GetString()!).Distinct(StringComparer.Ordinal).ToArray();
        if (digests.Length != 1) throw new InvalidOperationException("General model tag must resolve to one installed digest for the declared runner.");
        return (digests[0], new
        {
            details = details.Clone(),
            parameters = metadata.RootElement.TryGetProperty("parameters", out var parameters) ? parameters.GetString() : null,
            capabilities = metadata.RootElement.TryGetProperty("capabilities", out var capabilities) ? capabilities.Clone() : (JsonElement?)null,
        });
    }
}
