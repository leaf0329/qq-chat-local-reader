using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqMessageRecord
{
    public required string AccountId { get; init; }

    public required ConversationType ConversationType { get; init; }

    public required string ConversationId { get; init; }

    public required string ConversationDisplayName { get; init; }

    public required string StableMessageId { get; init; }

    public required DateTimeOffset TimestampUtc { get; init; }

    public required int RawDirection { get; init; }

    public required string SenderId { get; init; }

    public string? SenderDisplayName { get; init; }

    public QqMessageBody? Body { get; init; }

    public IReadOnlyList<string> ReplyTargetMessageIds { get; init; } = [];

    public override string ToString() =>
        $"{nameof(QqMessageRecord)} {{ ConversationType = {ConversationType}, BodyStatus = {Body?.Status.ToString() ?? "Missing"} }}";
}
