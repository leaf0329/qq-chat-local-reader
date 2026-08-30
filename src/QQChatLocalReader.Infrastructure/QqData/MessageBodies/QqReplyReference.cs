namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public sealed class QqReplyReference
{
    public long? MessageIdCandidate { get; init; }

    public long? SequenceCandidate { get; init; }

    public long? OriginalTimestamp { get; init; }

    public string? Summary { get; init; }

    public QqMessageSegment? EmbeddedContent { get; init; }

    public override string ToString() => $"{nameof(QqReplyReference)} {{ sensitive values omitted }}";
}
