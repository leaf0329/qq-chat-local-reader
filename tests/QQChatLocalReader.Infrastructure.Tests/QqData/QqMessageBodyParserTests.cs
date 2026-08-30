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

    [Fact]
    public void ParseKeepsConfirmedMediaMetadataScopedToItsMediaType()
    {
        var image = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 2);
            WriteInt32(output, 45003, 1);
            WriteString(output, 45402, "masked.jpg");
            WriteInt64(output, 45405, 1024);
            WriteInt32(output, 45411, 800);
            WriteInt32(output, 45412, 600);
            WriteString(output, 45812, "masked/image/path");
        });
        var video = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 5);
            WriteInt32(output, 45003, 7);
            WriteInt32(output, 45410, 12);
            WriteInt32(output, 45413, 1920);
            WriteInt32(output, 45414, 1080);
            WriteString(output, 45422, "masked/preview/path");
        });
        var file = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 3);
            WriteInt32(output, 45003, 11);
            WriteString(output, 45402, "masked.pdf");
            WriteString(output, 45403, "masked/sending/path");
            WriteInt64(output, 45405, 2048);
            WriteString(output, 45419, "pdf");
            WriteString(output, 45954, "masked/file/preview");
        });
        var voice = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 4);
            WriteInt32(output, 45003, 2);
            WriteInt32(output, 45410, 99);
        });
        var body = CreateMessage(output =>
        {
            WriteBytes(output, 40800, image);
            WriteBytes(output, 40800, video);
            WriteBytes(output, 40800, file);
            WriteBytes(output, 40800, voice);
        });

        var result = QqMessageBodyParser.Parse(body);

        Assert.Equal(QqMessageBodyParseStatus.Partial, result.Status);
        Assert.Equal(1, result.UnsupportedFieldCount);
        Assert.Collection(
            result.Segments,
            segment =>
            {
                Assert.Equal(1, segment.Media?.RawMediaSubtype);
                Assert.Equal("masked.jpg", segment.Media?.FileName);
                Assert.Equal(1024, segment.Media?.FileSize);
                Assert.Equal(800, segment.Media?.Width);
                Assert.Equal(600, segment.Media?.Height);
                Assert.Null(segment.Media?.DurationSeconds);
                Assert.DoesNotContain("masked.jpg", segment.Media!.ToString(), StringComparison.Ordinal);
            },
            segment =>
            {
                Assert.Equal(12, segment.Media?.DurationSeconds);
                Assert.Equal(1920, segment.Media?.Width);
                Assert.Equal(1080, segment.Media?.Height);
                Assert.Equal("masked/preview/path", segment.Media?.PreviewPath);
            },
            segment =>
            {
                Assert.Equal(11, segment.Media?.RawMediaSubtype);
                Assert.Equal("masked.pdf", segment.Media?.FileName);
                Assert.Equal("masked/sending/path", segment.Media?.LocalPath);
                Assert.Equal(2048, segment.Media?.FileSize);
                Assert.Equal("pdf", segment.Media?.FileExtension);
                Assert.Equal("masked/file/preview", segment.Media?.PreviewPath);
            },
            segment => Assert.Null(segment.Media?.DurationSeconds));
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
