namespace QQChatLocalReader.Application.Sync;

public sealed record SyncJobSnapshot(
    Guid JobId,
    SyncJobState State,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    int? MessageCount,
    string? ErrorCode)
{
    public override string ToString() =>
        $"{nameof(SyncJobSnapshot)} {{ JobId = {JobId}, State = {State}, MessageCount = {MessageCount?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown"} }}";
}
