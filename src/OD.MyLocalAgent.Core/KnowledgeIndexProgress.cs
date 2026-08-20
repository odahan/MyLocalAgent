namespace OD.MyLocalAgent.Core;

/// <summary>Reports one bounded Knowledge indexing phase.</summary>
public sealed record KnowledgeIndexProgress(
    string Phase,
    int CompletedDocuments,
    int TotalDocuments,
    int CompletedEmbeddings,
    int TotalEmbeddings);
