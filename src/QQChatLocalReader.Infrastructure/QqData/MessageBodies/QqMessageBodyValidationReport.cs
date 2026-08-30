namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public sealed class QqMessageBodyValidationReport
{
    internal QqMessageBodyValidationReport(
        int messageCount,
        int missingBodyCount,
        int completeBodyCount,
        int partialBodyCount,
        int malformedBodyCount,
        int segmentCount,
        int textSegmentCount,
        int emojiSegmentCount,
        int replySegmentCount,
        int unsupportedFieldCount)
    {
        MessageCount = messageCount;
        MissingBodyCount = missingBodyCount;
        CompleteBodyCount = completeBodyCount;
        PartialBodyCount = partialBodyCount;
        MalformedBodyCount = malformedBodyCount;
        SegmentCount = segmentCount;
        TextSegmentCount = textSegmentCount;
        EmojiSegmentCount = emojiSegmentCount;
        ReplySegmentCount = replySegmentCount;
        UnsupportedFieldCount = unsupportedFieldCount;
    }

    public int MessageCount { get; }

    public int MissingBodyCount { get; }

    public int CompleteBodyCount { get; }

    public int PartialBodyCount { get; }

    public int MalformedBodyCount { get; }

    public int SegmentCount { get; }

    public int TextSegmentCount { get; }

    public int EmojiSegmentCount { get; }

    public int ReplySegmentCount { get; }

    public int UnsupportedFieldCount { get; }

    public override string ToString() =>
        $"{nameof(QqMessageBodyValidationReport)} {{ Messages = {MessageCount}, Complete = {CompleteBodyCount}, Partial = {PartialBodyCount}, Malformed = {MalformedBodyCount} }}";
}
