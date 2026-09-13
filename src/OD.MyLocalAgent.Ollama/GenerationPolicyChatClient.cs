using System.Runtime.CompilerServices;
using System.ClientModel;
using System.Net;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OllamaSharp.Models;
using OllamaSharp.Models.Chat;
using OpenAIChatCompletionOptions = OpenAI.Chat.ChatCompletionOptions;

namespace OD.MyLocalAgent.Ollama;

/// <summary>
/// Applies one immutable generation policy to every request in an execution.
/// </summary>
public sealed class GenerationPolicyChatClient : IChatClient
{
    private readonly IChatClient _inner;
    private readonly bool _mapsOllamaContext;
    private readonly bool _reasoningEnabled;
    private readonly string? _reasoningEffort;
    private readonly bool _useProviderReasoningDefault;
    private readonly string _operation;

    /// <summary>
    /// Initializes a new policy-bound chat client.
    /// </summary>
    /// <param name="inner">The shared provider chat client.</param>
    /// <param name="policy">The immutable effective policy.</param>
    /// <param name="providerName">The selected provider name.</param>
    /// <param name="operation">The logical operation that initiated model calls.</param>
    /// <param name="reasoningEnabled">
    /// Whether reasoning is requested. When omitted, the effective policy is used.
    /// </param>
    public GenerationPolicyChatClient(
        IChatClient inner,
        EffectiveGenerationPolicy policy,
        string providerName,
        string operation = "llm",
        bool? reasoningEnabled = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        _inner = inner;
        Policy = policy;
        _mapsOllamaContext = string.Equals(
            providerName,
            AIOptions.OllamaProvider,
            StringComparison.OrdinalIgnoreCase);
        _useProviderReasoningDefault = reasoningEnabled is null && policy.UseProviderReasoningDefault;
        _reasoningEnabled = reasoningEnabled ?? policy.ReasoningEnabled;
        _reasoningEffort = _reasoningEnabled ? policy.ReasoningEffort : null;
        _operation = operation;
    }

    /// <summary>
    /// Gets the immutable policy applied by this wrapper.
    /// </summary>
    public EffectiveGenerationPolicy Policy { get; }

    /// <inheritdoc />
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
        ChatResponse response;
        if (_useProviderReasoningDefault)
        {
            response = await _inner.GetResponseAsync(
                messageList,
                CreateOptions(options, applyReasoning: false),
                cancellationToken).ConfigureAwait(false);
        }
        else try
        {
            response = await _inner.GetResponseAsync(
                messageList,
                CreateOptions(options, applyReasoning: true),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldRetryWithoutReasoning(exception, cancellationToken))
        {
            response = await _inner.GetResponseAsync(
                messageList,
                CreateOptions(options, applyReasoning: false),
                cancellationToken).ConfigureAwait(false);
        }
        TaskTimingCollector.Current.Value?.RecordLlmUsage(_operation, response.Usage);
        return response;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
        UsageDetails? usage = null;
        if (_useProviderReasoningDefault)
        {
            await foreach (var update in _inner.GetStreamingResponseAsync(
                messageList,
                CreateOptions(options, applyReasoning: false),
                cancellationToken).ConfigureAwait(false))
            {
                usage = update.Contents.OfType<UsageContent>()
                    .LastOrDefault()?.Details ?? usage;
                yield return update;
            }
        }
        else try
        {
            await foreach (var update in GetStreamingResponseWithFallbackAsync(
                messageList,
                options,
                cancellationToken).ConfigureAwait(false))
            {
                usage = update.Contents.OfType<UsageContent>()
                    .LastOrDefault()?.Details ?? usage;
                yield return update;
            }
        }
        finally
        {
            TaskTimingCollector.Current.Value?.RecordLlmUsage(_operation, usage);
        }
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceKey is null && serviceType.IsInstanceOfType(this)
            ? this
            : _inner.GetService(serviceType, serviceKey);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // The provider client is shared and owned by dependency injection.
    }

    private ChatOptions CreateOptions(ChatOptions? options, bool applyReasoning)
    {
        var effective = options?.Clone() ?? new ChatOptions();
        effective.Temperature = (float)Policy.Temperature;
        effective.TopP = (float)Policy.TopP;
        if (applyReasoning)
        {
            effective.Reasoning = new ReasoningOptions
            {
                Effort = ResolveStandardEffort(),
                Output = _reasoningEnabled ? ReasoningOutput.Full : ReasoningOutput.None,
            };

            if (!_mapsOllamaContext && _reasoningEffort is not null)
            {
                ApplyOpenAIReasoningEffort(effective, _reasoningEffort);
            }
        }
        else
        {
            effective.Reasoning = null;
            effective.AdditionalProperties?.Remove(OllamaOption.Think.Name);
        }

        if (_mapsOllamaContext)
        {
            effective.AddOllamaOption(
                OllamaOption.NumCtx,
                Policy.ContextWindowTokens);
            if (applyReasoning)
            {
                if (_reasoningEffort is null)
                {
                    effective.AddOllamaOption(OllamaOption.Think, _reasoningEnabled);
                }
                else
                {
                    effective.AddOllamaOption(OllamaOption.Think, new ThinkValue(_reasoningEffort));
                }
            }
        }

        return effective;
    }

    private ReasoningEffort? ResolveStandardEffort()
    {
        if (!_reasoningEnabled)
        {
            return ReasoningEffort.None;
        }

        if (_reasoningEffort is null)
        {
            return ReasoningEffort.Medium;
        }

        if (_mapsOllamaContext)
        {
            return null;
        }

        return _reasoningEffort.ToLowerInvariant() switch
        {
            "low" => ReasoningEffort.Low,
            "medium" => ReasoningEffort.Medium,
            "high" => ReasoningEffort.High,
            "xhigh" => ReasoningEffort.ExtraHigh,
            _ => null,
        };
    }

    private static void ApplyOpenAIReasoningEffort(
        ChatOptions options,
        string reasoningEffort)
    {
        var precedingFactory = options.RawRepresentationFactory;
        options.RawRepresentationFactory = client =>
        {
            var raw = precedingFactory?.Invoke(client);
            if (raw is not null and not OpenAIChatCompletionOptions)
            {
                throw new InvalidOperationException(
                    "The OpenAI reasoning-effort override requires ChatCompletionOptions raw options.");
            }

            var completionOptions = raw as OpenAIChatCompletionOptions ?? new OpenAIChatCompletionOptions();
#pragma warning disable OPENAI001
            completionOptions.ReasoningEffortLevel = reasoningEffort;
#pragma warning restore OPENAI001
            return completionOptions;
        };
    }

    private async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseWithFallbackAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var enumerator = _inner.GetStreamingResponseAsync(
                messages,
                CreateOptions(options, applyReasoning: true),
                cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        var useFallback = false;
        var hasNext = false;
        try
        {
            hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (ShouldRetryWithoutReasoning(exception, cancellationToken))
        {
            useFallback = true;
        }

        if (useFallback)
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
            await foreach (var update in _inner.GetStreamingResponseAsync(
                messages,
                CreateOptions(options, applyReasoning: false),
                cancellationToken).ConfigureAwait(false))
            {
                yield return update;
            }

            yield break;
        }

        try
        {
            while (hasNext)
            {
                yield return enumerator.Current;
                hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            await enumerator.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static bool ShouldRetryWithoutReasoning(
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || exception is OperationCanceledException)
        {
            return false;
        }

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is ClientResultException { Status: 400 or 422 } ||
                current is HttpRequestException
                {
                    StatusCode: HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity,
                })
            {
                return true;
            }

            if (current.Message.Contains("reasoning", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("reasoning_effort", StringComparison.OrdinalIgnoreCase) ||
                current.Message.Contains("think", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

}
