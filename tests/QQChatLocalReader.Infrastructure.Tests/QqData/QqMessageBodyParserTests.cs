using Google.Protobuf;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.Tests.QqData;

public sealed class QqMessageBodyParserTests
{
    [Fact]
    public void ParsePreservesTextEmojiAndReplySegmentOrder()
    {
        var embedded = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 1);
            WriteString(output, 45101, "quoted text");
        });
        var text = CreateMessage(output =>
        {
            WriteInt64(output, 45001, 10);
            WriteInt32(output, 45002, 1);
            WriteString(output, 45101, "hello");
        });
        var emoji = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 6);
            WriteInt32(output, 47601, 123);
            WriteString(output, 47602, "/masked-face");
        });
        var reply = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 7);
            WriteInt64(output, 47401, 99);
            WriteInt64(output, 47402, 88);
            WriteInt64(output, 47404, 1_700_000_000);
            WriteString(output, 47413, "masked summary");
            WriteBytes(output, 47710, embedded);
        });
        var body = CreateMessage(output =>
        {
            WriteBytes(output, 40800, text);
            WriteBytes(output, 40800, emoji);
            WriteBytes(output, 40800, reply);
        });

        var result = QqMessageBodyParser.Parse(body);

        Assert.Equal(QqMessageBodyParseStatus.Complete, result.Status);
        Assert.Collection(
            result.Segments,
            segment =>
            {
                Assert.Equal(QqMessageContentType.Text, segment.ContentType);
                Assert.Equal("hello", segment.Text);
                Assert.Equal(10, segment.SegmentId);
            },
            segment =>
            {
                Assert.Equal(QqMessageContentType.QqFace, segment.ContentType);
                Assert.Equal(123, segment.EmojiId);
                Assert.Equal("/masked-face", segment.EmojiText);
            },
            segment =>
            {
                Assert.Equal(QqMessageContentType.Reply, segment.ContentType);
                Assert.Equal(99, segment.Reply?.MessageIdCandidate);
                Assert.Equal(88, segment.Reply?.SequenceCandidate);
                Assert.Equal(1_700_000_000, segment.Reply?.OriginalTimestamp);
                Assert.Equal("masked summary", segment.Reply?.Summary);
                Assert.Equal("quoted text", segment.Reply?.EmbeddedContent?.Text);
            });
        Assert.DoesNotContain("hello", result.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("masked summary", result.Segments[2].Reply!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ParseMarksInvalidUtf8AsPartialAndCountsUnknownFields()
    {
        var segment = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 1);
            WriteBytes(output, 45101, [0xff, 0xfe]);
            WriteInt32(output, 49999, 1);
        });
        var body = CreateMessage(output => WriteBytes(output, 40800, segment));

        var result = QqMessageBodyParser.Parse(body);

        var parsed = Assert.Single(result.Segments);
        Assert.Equal(QqMessageBodyParseStatus.Partial, result.Status);
        Assert.Null(parsed.Text);
        Assert.Equal(1, result.UnsupportedFieldCount);
    }

    [Fact]
    public void ParseRejectsTruncatedWirePayload()
    {
        var result = QqMessageBodyParser.Parse([0x82, 0xf6, 0x13, 0x05, 0x01]);

        Assert.Equal(QqMessageBodyParseStatus.Malformed, result.Status);
        Assert.Empty(result.Segments);
    }

    private static byte[] CreateMessage(Action<CodedOutputStream> write)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            write(output);
            output.Flush();
        }

        return stream.ToArray();
    }

    private static void WriteInt32(CodedOutputStream output, int fieldNumber, int value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteInt32(value);
    }

    private static void WriteInt64(CodedOutputStream output, int fieldNumber, long value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteInt64(value);
    }

    private static void WriteString(CodedOutputStream output, int fieldNumber, string value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        output.WriteString(value);
    }

    private static void WriteBytes(CodedOutputStream output, int fieldNumber, byte[] value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(value));
    }
}
