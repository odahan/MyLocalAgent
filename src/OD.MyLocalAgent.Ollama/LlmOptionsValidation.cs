namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Centralizes configuration validation shared by the supported LLM providers.
/// </summary>
internal static class LlmOptionsValidation
{
    public static string? ValidateAI(AIOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Provider))
        {
            return "AI:Provider is required. Use 'Ollama' or 'OpenAI'.";
        }

        var generationError =
            GenerationPolicyValidation.ValidateDefaults(options.GenerationDefaults);
        return generationError is null
            ? null
            : $"AI:GenerationDefaults is invalid. {generationError}";
    }

    public static string? ValidateOllama(OllamaOptions options)
    {
        return ValidateProvider(
            OllamaOptions.SectionName,
            options.Endpoint,
            options.Model,
            options.TimeoutSeconds);
    }

    public static string? ValidateOpenAI(OpenAIOptions options, bool requireApiKey)
    {
        var validation = ValidateProvider(
            OpenAIOptions.SectionName,
            options.Endpoint,
            options.Model,
            options.TimeoutSeconds);

        if (validation is not null)
        {
            return validation;
        }

        if (options.SupportsImageOutput && string.IsNullOrWhiteSpace(options.ImageModel))
        {
            return "OpenAI:ImageModel is required when image output is enabled.";
        }

        if (options.SupportsAudioOutput &&
            (string.IsNullOrWhiteSpace(options.AudioModel) || string.IsNullOrWhiteSpace(options.AudioVoice)))
        {
            return "OpenAI:AudioModel and OpenAI:AudioVoice are required when audio output is enabled.";
        }

        return requireApiKey && string.IsNullOrWhiteSpace(options.ApiKey)
            ? "OpenAI:ApiKey is required when AI:Provider is 'OpenAI'. Configure it in the protected secret store."
            : null;
    }

    public static Uri ParseEndpoint(string endpoint, string sectionName)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new LlmConfigurationException(
                $"{sectionName}:Endpoint must be an absolute HTTP or HTTPS URI.");
        }

        var builder = new UriBuilder(uri);
        if (!builder.Path.EndsWith('/'))
        {
            builder.Path += "/";
        }

        return builder.Uri;
    }

    private static string? ValidateProvider(
        string sectionName,
        string endpoint,
        string model,
        int timeoutSeconds)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return $"{sectionName}:Endpoint must be an absolute HTTP or HTTPS URI.";
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            return $"{sectionName}:Model is required.";
        }

        return timeoutSeconds <= 0
            ? $"{sectionName}:TimeoutSeconds must be greater than zero."
            : null;
    }
}
