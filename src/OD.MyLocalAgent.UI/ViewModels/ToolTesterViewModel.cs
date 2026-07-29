using System.Diagnostics;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OD.MyLocalAgent.Core;

namespace OD.MyLocalAgent.UI.ViewModels;

/// <summary>
/// Invokes one Tool Map alias with user-supplied JSON arguments.
/// </summary>
public sealed partial class ToolTesterViewModel :
    ObservableObject,
    IActivatablePage
{
    private static readonly JsonSerializerOptions PrettyJsonOptions =
        new()
        {
            WriteIndented = true,
        };

    private readonly IToolRegistry _toolRegistry;
    private readonly IIncidentReporter _incidentReporter;

    [ObservableProperty]
    private ToolAliasItem? _selectedAlias;

    [ObservableProperty]
    private string _argumentsJson = "{}";

    [ObservableProperty]
    private string _argumentHint = "Select an alias.";

    [ObservableProperty]
    private string _resultText = "Tool output will appear here.";

    [ObservableProperty]
    private string _durationText = "Not run";

    [ObservableProperty]
    private string _statusState = "Unknown";

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolTesterViewModel"/> class.
    /// </summary>
    public ToolTesterViewModel(
        IToolRegistry toolRegistry,
        IIncidentReporter incidentReporter)
    {
        _toolRegistry = toolRegistry;
        _incidentReporter = incidentReporter;
        Aliases = toolRegistry.Definitions
            .Where(static definition => definition.IsAgentTool)
            .OrderBy(static definition => definition.ToolGroup)
            .ThenBy(static definition => definition.Alias)
            .Select(
                static definition => new ToolAliasItem(
                    definition.Alias,
                    definition.ToolGroup,
                    definition.Arguments,
                    definition.SafetyFlag))
            .ToArray();
    }

    /// <summary>
    /// Gets the invocable aliases grouped by tool group.
    /// </summary>
    public IReadOnlyList<ToolAliasItem> Aliases { get; }

    /// <inheritdoc />
    public void Activate()
    {
        SelectedAlias ??= Aliases.FirstOrDefault();
    }

    partial void OnSelectedAliasChanged(ToolAliasItem? value)
    {
        if (value is null)
        {
            ArgumentHint = "Select an alias.";
            ArgumentsJson = "{}";
            return;
        }

        ArgumentHint = value.Arguments.Count == 0
            ? "This alias accepts no arguments."
            : $"Expected JSON properties: {string.Join(", ", value.Arguments)}";
        var template = value.Arguments.ToDictionary(
            static argument => argument,
            static _ => string.Empty,
            StringComparer.Ordinal);
        ArgumentsJson = JsonSerializer.Serialize(template, PrettyJsonOptions);
        ResultText = value.SafetyFlag is null
            ? "Ready to run."
            : $"Safety gate: {value.SafetyFlag}";
        DurationText = "Not run";
        StatusState = "Unknown";
    }

    [RelayCommand]
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        if (SelectedAlias is null)
        {
            ResultText = "Select a Tool Map alias.";
            return;
        }

        IsBusy = true;
        StatusState = "Checking";
        ResultText = "Running tool…";
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var document = JsonDocument.Parse(
                string.IsNullOrWhiteSpace(ArgumentsJson)
                    ? "{}"
                    : ArgumentsJson);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new JsonException("Tool arguments must be a JSON object.");
            }

            var result = await _toolRegistry
                .InvokeAsync(
                    SelectedAlias.Alias,
                    document.RootElement.Clone(),
                    [SelectedAlias.Group],
                    cancellationToken)
                .ConfigureAwait(true);
            stopwatch.Stop();
            ResultText = PrettyPrint(result);
            DurationText = $"{stopwatch.ElapsedMilliseconds} ms";
            StatusState = "Healthy";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            ResultText = "Tool invocation canceled.";
            DurationText = $"{stopwatch.ElapsedMilliseconds} ms";
            StatusState = "Unknown";
        }
        catch (Exception exception) when (Find<SafetyException>(exception) is { } safety)
        {
            stopwatch.Stop();
            ResultText =
                $"{safety.Message}{Environment.NewLine}{Environment.NewLine}" +
                $"Open Settings → General and enable {safety.RequiredFlag}.";
            DurationText = $"{stopwatch.ElapsedMilliseconds} ms · blocked";
            StatusState = "Blocked";
        }
        catch (Exception exception)
            when (exception is JsonException or
                ArgumentException or
                WorkflowValidationException)
        {
            stopwatch.Stop();
            ResultText = $"{exception.GetType().Name}: {exception.Message}";
            DurationText = $"{stopwatch.ElapsedMilliseconds} ms";
            StatusState = "Unhealthy";
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            ResultText = $"{exception.GetType().Name}: {exception.Message}";
            DurationText = $"{stopwatch.ElapsedMilliseconds} ms";
            StatusState = "Unhealthy";
            _ = await _incidentReporter
                .ReportAsync(
                    exception,
                    "Tool tester invocation",
                    CancellationToken.None)
                .ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string PrettyPrint(string result)
    {
        try
        {
            using var document = JsonDocument.Parse(result);
            return JsonSerializer.Serialize(
                document.RootElement,
                PrettyJsonOptions);
        }
        catch (JsonException)
        {
            return result;
        }
    }

    private static TException? Find<TException>(Exception exception)
        where TException : Exception
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is TException typed)
            {
                return typed;
            }

            if (current is AggregateException aggregate)
            {
                var nested = aggregate.InnerExceptions
                    .Select(Find<TException>)
                    .FirstOrDefault(static candidate => candidate is not null);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
