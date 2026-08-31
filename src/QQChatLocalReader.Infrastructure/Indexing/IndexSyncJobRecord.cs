namespace QQChatLocalReader.Infrastructure.Indexing;

public sealed record IndexSyncJobRecord(
    Guid JobId,
    int State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    int? MessageCount,
    string? ErrorCode,
    string RequestJson);
