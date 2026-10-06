namespace OD.MyLocalAgent.Connectors;

/// <summary>A bounded Gmail collection with explicit pagination coverage and no invented total.</summary>
public sealed record GmailReadBatch(IReadOnlyList<EmailMessageDto> Messages, bool CollectionPartial);
