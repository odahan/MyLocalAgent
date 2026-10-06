using OD.MyLocalAgent.Ollama;

namespace OD.MyLocalAgent.Core;

/// <summary>Provides one isolated conventional local LLM opinion without screening, summaries, tools, or RAG.</summary>
public interface ILightweightMailClassifier
{
    /// <summary>Classifies exactly one bounded mail using fixed local generation and the authored questions.</summary>
    Task<LocalDecisionResponse> ClassifyAsync(LocalDecisionRequest request, DecisionQuestionSet questions, CancellationToken cancellationToken);
}

/// <summary>Reuses the same strict local request for both fallback and independent arbitration.</summary>
public sealed class LightweightMailClassifier(ILocalDecisionClient client) : ILightweightMailClassifier
{
    /// <inheritdoc />
    public Task<LocalDecisionResponse> ClassifyAsync(LocalDecisionRequest request, DecisionQuestionSet questions, CancellationToken cancellationToken) =>
        client.ClassifyAsync(request, questions.LlmSchema(), cancellationToken);
}
