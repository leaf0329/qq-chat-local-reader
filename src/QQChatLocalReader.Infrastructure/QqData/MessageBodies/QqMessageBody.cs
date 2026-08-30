namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public sealed class QqMessageBody
{
    public QqMessageBody(
        QqMessageBodyParseStatus status,
        IReadOnlyList<QqMessageSegment> segments,
        int unsupportedFieldCount)
    {
        Status = status;
        Segments = segments;
        UnsupportedFieldCount = unsupportedFieldCount;
    }

    public QqMessageBodyParseStatus Status { get; }

    public IReadOnlyList<QqMessageSegment> Segments { get; }

    public int UnsupportedFieldCount { get; }

    public override string ToString() =>
        $"{nameof(QqMessageBody)} {{ Status = {Status}, Segments = {Segments.Count}, UnsupportedFields = {UnsupportedFieldCount} }}";
}
