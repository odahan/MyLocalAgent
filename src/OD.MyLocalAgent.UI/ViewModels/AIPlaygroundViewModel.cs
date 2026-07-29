using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Options;
using OD.MyLocalAgent.Core;
using OD.MyLocalAgent.Ollama;
using OD.MyLocalAgent.UI.Messages;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Streams raw completions from the currently selected language-model provider.
/// </summary>
public sealed partial class AIPlaygroundViewModel :
    ObservableRecipient,
    IActivatablePage,
    IRecipient<ProviderConfigurationChangedMessage>
{
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly IOptionsMonitor<AIOptions> _aiOptions;
    private readonly IOptionsMonitor<OllamaOptions> _ollamaOptions;
    private readonly IOptionsMonitor<OpenAIOptions> _openAIOptions;
    private readonly IIncidentReporter _incidentReporter;

    [ObservableProperty]
    private string _prompt = string.Empty;

    [ObservableProperty]
    private string _output = "Raw provider output will stream here.";

    [ObservableProperty]
    private string _providerBadge = "Provider not checked";

    [ObservableProperty]
    private string _healthText = "Health has not been checked.";

    [ObservableProperty]
    private string _healthState = "Unknown";

    [ObservableProperty]
    private int _chunkCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isStreaming;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIPlaygroundViewModel"/> class.
    /// </summary>
    public AIPlaygroundViewModel(
        ILlmClientFactory llmClientFactory,
        IOptionsMonitor<AIOptions> aiOptions,
        IOptionsMonitor<OllamaOptions> ollamaOptions,
        IOptionsMonitor<OpenAIOptions> openAIOptions,
        IIncidentReporter incidentReporter,
        IMessenger messenger)
        : base(messenger)
    {
        _llmClientFactory = llmClientFactory;
        _aiOptions = aiOptions;
        _ollamaOptions = ollamaOptions;
        _openAIOptions = openAIOptions;
        _incidentReporter = incidentReporter;
        IsActive = true;
    }

    /// <inheritdoc />
    public void Activate()
    {
        if (!RefreshProviderCommand.IsRunning)
        {
            RefreshProviderCommand.Execute(null);
        }
    }

    /// <inheritdoc />
    public void Receive(ProviderConfigurationChangedMessage message)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            return;
        }

        _ = dispatcher.BeginInvoke(
            () =>
            {
                if (!RefreshProviderCommand.IsRunning)
                {
                    RefreshProviderCommand.Execute(null);
                }
            });
    }

    [RelayCommand]
    private async Task RefreshProviderAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        HealthState = "Checking";
        HealthText = "Checking the active provider and configured model…";
        ProviderBadge = BuildProviderBadge();
        try
        {
            var provider = _llmClientFactory.Create();
            var health = await provider
                .CheckHealthAsync(cancellationToken)
                .ConfigureAwait(true);
            var healthy = health.IsReachable && health.ModelAvailable;
            HealthState = healthy ? "Healthy" : "Unhealthy";
            HealthText = healthy
                ? $"Healthy · {health.LatencyMs} ms"
                : $"{health.Error ?? "Provider or model unavailable."} · {health.LatencyMs} ms";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HealthState = "Unknown";
            HealthText = "Health check canceled.";
        }
        catch (Exception exception)
        {
            HealthState = "Unhealthy";
            HealthText = exception.Message;
            _ = await _incidentReporter
                .ReportAsync(
                    exception,
                    "AI playground provider health",
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task GenerateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Prompt))
        {
            HealthState = "Unhealthy";
            HealthText = "Enter a prompt before generating.";
            return;
        }

        IsStreaming = true;
        Output = string.Empty;
        ChunkCount = 0;
        try
        {
            var provider = _llmClientFactory.Create();
            ProviderBadge = BuildProviderBadge();
            HealthState = "Checking";
            HealthText = $"Streaming raw completion from {provider.ProviderName}…";
            await foreach (var chunk in provider
                               .GenerateStreamingAsync(
                                   Prompt,
                                   cancellationToken)
                               .ConfigureAwait(true))
            {
                Output += chunk;
                ChunkCount++;
            }

            HealthState = "Healthy";
            HealthText = $"Generation completed with {ChunkCount} chunk(s).";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            HealthState = "Unknown";
            HealthText = $"Generation canceled after {ChunkCount} chunk(s).";
        }
        catch (Exception exception)
        {
            HealthState = "Unhealthy";
            HealthText = exception.Message;
            _ = await _incidentReporter
                .ReportAsync(
                    exception,
                    "AI playground generation",
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        finally
        {
            IsStreaming = false;
        }
    }

    private string BuildProviderBadge()
    {
        var provider = _aiOptions.CurrentValue.Provider;
        var model = string.Equals(
            provider,
            AIOptions.OpenAIProvider,
            StringComparison.OrdinalIgnoreCase)
            ? _openAIOptions.CurrentValue.Model
            : _ollamaOptions.CurrentValue.Model;
        return $"{provider} · {model}";
    }
}
