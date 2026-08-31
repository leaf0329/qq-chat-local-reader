using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Application.Mcp;

internal static class McpMessageFormatter
{
    public static McpMessageDto Create(QqMessageRecord message) => new(
        message.AccountId,
        message.ConversationType.ToString().ToLowerInvariant(),
        message.ConversationId,
        message.ConversationDisplayName,
        message.StableMessageId,
        message.TimestampUtc,
        message.SenderId,
        message.SenderDisplayName,
        string.Join("\n", message.Body?.Segments.Select(FormatSegment) ?? []),
        message.ReplyTargetMessageIds);

    private static string FormatSegment(QqMessageSegment segment) =>
        segment.Text ?? segment.EmojiText ?? segment.Reply?.Summary ??
        (segment.Media?.FileName is { Length: > 0 } fileName
            ? $"[{segment.ContentType}: {fileName}]"
            : $"[{segment.ContentType}]");
}

public sealed record McpMessageDto(
    string AccountId,
    string ConversationType,
    string ConversationId,
    string ConversationName,
    string MessageId,
    DateTimeOffset TimestampUtc,
    string SenderId,
    string? SenderName,
    string Text,
    IReadOnlyList<string> ReplyTargetMessageIds);
