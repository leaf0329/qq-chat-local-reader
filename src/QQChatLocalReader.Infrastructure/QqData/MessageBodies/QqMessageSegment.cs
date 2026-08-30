namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public sealed class QqMessageSegment
{
    public long? SegmentId { get; init; }

    public required int RawContentType { get; init; }

    public QqMessageContentType ContentType => Enum.IsDefined(typeof(QqMessageContentType), RawContentType)
        ? (QqMessageContentType)RawContentType
        : QqMessageContentType.Unknown;

    public string? Text { get; init; }

    public int? EmojiId { get; init; }

    public string? EmojiText { get; init; }

    public QqReplyReference? Reply { get; init; }

    public override string ToString() => $"{nameof(QqMessageSegment)} {{ ContentType = {ContentType} }}";
}
