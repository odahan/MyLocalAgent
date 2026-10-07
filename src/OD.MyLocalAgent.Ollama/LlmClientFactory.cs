using Microsoft.Extensions.Options;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Selects a provider from reloadable configuration on every call.
/// </summary>
public sealed class LlmClientFactory : ILlmClientFactory
{
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAIOptions;
    private readonly OllamaClientService _ollamaClient;
    private readonly OpenAIClientService _openAIClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="LlmClientFactory"/> class.
    /// </summary>
    /// <param name="aiOptions">The reloadable provider selection.</param>
    /// <param name="ollamaOptions">The reloadable Ollama configuration.</param>
    /// <param name="openAIOptions">The reloadable OpenAI configuration.</param>
    /// <param name="ollamaClient">The Ollama provider service.</param>
    /// <param name="openAIClient">The OpenAI provider service.</param>
    public LlmClientFactory(
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAIOptions,
        OllamaClientService ollamaClient,
        OpenAIClientService openAIClient)
    {
        ArgumentNullException.ThrowIfNull(aiOptions);
        ArgumentNullException.ThrowIfNull(ollamaOptions);
        ArgumentNullException.ThrowIfNull(openAIOptions);
        ArgumentNullException.ThrowIfNull(ollamaClient);
        ArgumentNullException.ThrowIfNull(openAIClient);

        _aiOptions = aiOptions;
        _ollamaOptions = ollamaOptions;
        _openAIOptions = openAIOptions;
        _ollamaClient = ollamaClient;
        _openAIClient = openAIClient;
    }

    /// <inheritdoc />
    public ILlmClientService Create()
    {
        AIOptions options;
        try
        {
            options = _aiOptions.CurrentValue;
        }
        catch (OptionsValidationException exception)
        {
            throw new LlmConfigurationException(
                $"The AI provider configuration is invalid: {exception.Message}",
                exception);
        }

        var providerValidation = LlmOptionsValidation.ValidateAI(options);
        if (providerValidation is not null)
        {
            throw new LlmConfigurationException(providerValidation);
        }

        if (string.Equals(
            options.Provider,
            AIOptions.OllamaProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            ValidateOllama();
            return _ollamaClient;
        }

        if (string.Equals(
            options.Provider,
            AIOptions.OpenAIProvider,
            StringComparison.OrdinalIgnoreCase))
        {
            ValidateOpenAI();
            return _openAIClient;
        }

        throw new LlmConfigurationException(
            $"AI:Provider '{options.Provider}' is not supported. Use 'Ollama' or 'OpenAI'.");
    }

    private void ValidateOllama()
    {
        try
        {
            var validation = LlmOptionsValidation.ValidateOllama(_ollamaOptions.CurrentValue);
            if (validation is not null)
            {
                throw new LlmConfigurationException(validation);
            }
        }
        catch (OptionsValidationException exception)
        {
            throw new LlmConfigurationException(
                $"The Ollama provider configuration is invalid: {exception.Message}",
                exception);
        }
    }

    /// <inheritdoc />
    public LlmClientSnapshot Capture()
    {
        return _aiOptions.CurrentValue.Provider switch
        {
            AIOptions.OllamaProvider => _ollamaClient.Capture(),
            AIOptions.OpenAIProvider => _openAIClient.Capture(),
            _ => throw new NotSupportedException("The selected central provider does not support structured decisions."),
        };
    }

    private void ValidateOpenAI()
    {
        try
        {
            var validation = LlmOptionsValidation.ValidateOpenAI(
                _openAIOptions.CurrentValue,
                requireApiKey: true);
            if (validation is not null)
            {
                throw new LlmConfigurationException(validation);
            }
        }
        catch (OptionsValidationException exception)
        {
            throw new LlmConfigurationException(
                $"The OpenAI provider configuration is invalid: {exception.Message}",
                exception);
        }
    }
}
