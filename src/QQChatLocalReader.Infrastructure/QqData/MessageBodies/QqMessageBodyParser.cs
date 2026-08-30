using System.Text;
using Google.Protobuf;

namespace QQChatLocalReader.Infrastructure.QqData.MessageBodies;

public static class QqMessageBodyParser
{
    private const int BodyContentField = 40800;
    private const int SegmentIdField = 45001;
    private const int ContentTypeField = 45002;
    private const int MediaSubtypeField = 45003;
    private const int TextField = 45101;
    private const int FileNameField = 45402;
    private const int LocalSendingPathField = 45403;
    private const int FileSizeField = 45405;
    private const int VideoDurationField = 45410;
    private const int ImageWidthField = 45411;
    private const int ImageHeightField = 45412;
    private const int VideoWidthField = 45413;
    private const int VideoHeightField = 45414;
    private const int FileExtensionField = 45419;
    private const int VideoPreviewPathField = 45422;
    private const int LocalCachePathField = 45812;
    private const int FilePreviewPathField = 45954;
    private const int ReplyMessageIdField = 47401;
    private const int ReplySequenceField = 47402;
    private const int ReplyTimestampField = 47404;
    private const int ReplySummaryField = 47413;
    private const int EmojiIdField = 47601;
    private const int EmojiTextField = 47602;
    private const int EmbeddedReplyField = 47710;
    private const int MaximumBodySize = 16 * 1024 * 1024;
    private const int MaximumSegments = 256;
    private const int MaximumNestingDepth = 4;

    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static QqMessageBody Parse(byte[] payload)
    {
        ArgumentNullException.ThrowIfNull(payload);
        if (payload.Length is 0 or > MaximumBodySize)
        {
            return new QqMessageBody(QqMessageBodyParseStatus.Malformed, [], 0);
        }

        var state = new ParseState();
        try
        {
            using var input = new CodedInputStream(payload);
            while (input.ReadTag() is var tag && tag != 0)
            {
                if (WireFormat.GetTagFieldNumber(tag) == BodyContentField &&
                    WireFormat.GetTagWireType(tag) == WireFormat.WireType.LengthDelimited)
                {
                    if (state.Segments.Count >= MaximumSegments)
                    {
                        state.IsPartial = true;
                        input.SkipLastField();
                        continue;
                    }

                    state.Segments.Add(ParseSegment(input.ReadBytes(), 0, state));
                }
                else
                {
                    state.UnsupportedFieldCount++;
                    input.SkipLastField();
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidProtocolBufferException or InvalidOperationException or OverflowException)
        {
            return new QqMessageBody(
                QqMessageBodyParseStatus.Malformed,
                state.Segments.ToArray(),
                state.UnsupportedFieldCount);
        }

        var status = state.Segments.Count == 0
            ? state.UnsupportedFieldCount > 0
                ? QqMessageBodyParseStatus.Partial
                : QqMessageBodyParseStatus.Malformed
            : state.IsPartial || state.UnsupportedFieldCount > 0
                ? QqMessageBodyParseStatus.Partial
                : QqMessageBodyParseStatus.Complete;
        return new QqMessageBody(status, state.Segments.ToArray(), state.UnsupportedFieldCount);
    }

    private static QqMessageSegment ParseSegment(
        ByteString payload,
        int depth,
        ParseState state)
    {
        long? segmentId = null;
        var contentType = 0;
        int? mediaSubtype = null;
        string? text = null;
        string? fileName = null;
        string? localSendingPath = null;
        string? localCachePath = null;
        long? fileSize = null;
        int? videoDuration = null;
        int? imageWidth = null;
        int? imageHeight = null;
        int? videoWidth = null;
        int? videoHeight = null;
        string? fileExtension = null;
        string? videoPreviewPath = null;
        string? filePreviewPath = null;
        int? emojiId = null;
        string? emojiText = null;
        long? replyMessageId = null;
        long? replySequence = null;
        long? replyTimestamp = null;
        string? replySummary = null;
        QqMessageSegment? embeddedReply = null;

        using var input = new CodedInputStream(payload.ToByteArray());
        while (input.ReadTag() is var tag && tag != 0)
        {
            var fieldNumber = WireFormat.GetTagFieldNumber(tag);
            var wireType = WireFormat.GetTagWireType(tag);
            switch (fieldNumber, wireType)
            {
                case (SegmentIdField, WireFormat.WireType.Varint):
                    segmentId = input.ReadInt64();
                    break;
                case (ContentTypeField, WireFormat.WireType.Varint):
                    contentType = input.ReadInt32();
                    break;
                case (MediaSubtypeField, WireFormat.WireType.Varint):
                    mediaSubtype = input.ReadInt32();
                    break;
                case (TextField, WireFormat.WireType.LengthDelimited):
                    text = ReadUtf8(input, state);
                    break;
                case (FileNameField, WireFormat.WireType.LengthDelimited):
                    fileName = ReadUtf8(input, state);
                    break;
                case (LocalSendingPathField, WireFormat.WireType.LengthDelimited):
                    localSendingPath = ReadUtf8(input, state);
                    break;
                case (FileSizeField, WireFormat.WireType.Varint):
                    fileSize = input.ReadInt64();
                    break;
                case (VideoDurationField, WireFormat.WireType.Varint):
                    videoDuration = input.ReadInt32();
                    break;
                case (ImageWidthField, WireFormat.WireType.Varint):
                    imageWidth = input.ReadInt32();
                    break;
                case (ImageHeightField, WireFormat.WireType.Varint):
                    imageHeight = input.ReadInt32();
                    break;
                case (VideoWidthField, WireFormat.WireType.Varint):
                    videoWidth = input.ReadInt32();
                    break;
                case (VideoHeightField, WireFormat.WireType.Varint):
                    videoHeight = input.ReadInt32();
                    break;
                case (FileExtensionField, WireFormat.WireType.LengthDelimited):
                    fileExtension = ReadUtf8(input, state);
                    break;
                case (VideoPreviewPathField, WireFormat.WireType.LengthDelimited):
                    videoPreviewPath = ReadUtf8(input, state);
                    break;
                case (LocalCachePathField, WireFormat.WireType.LengthDelimited):
                    localCachePath = ReadUtf8(input, state);
                    break;
                case (FilePreviewPathField, WireFormat.WireType.LengthDelimited):
                    filePreviewPath = ReadUtf8(input, state);
                    break;
                case (EmojiIdField, WireFormat.WireType.Varint):
                    emojiId = input.ReadInt32();
                    break;
                case (EmojiTextField, WireFormat.WireType.LengthDelimited):
                    emojiText = ReadUtf8(input, state);
                    break;
                case (ReplyMessageIdField, WireFormat.WireType.Varint):
                    replyMessageId = input.ReadInt64();
                    break;
                case (ReplySequenceField, WireFormat.WireType.Varint):
                    replySequence = input.ReadInt64();
                    break;
                case (ReplyTimestampField, WireFormat.WireType.Varint):
                    replyTimestamp = input.ReadInt64();
                    break;
                case (ReplySummaryField, WireFormat.WireType.LengthDelimited):
                    replySummary = ReadUtf8(input, state);
                    break;
                case (EmbeddedReplyField, WireFormat.WireType.LengthDelimited) when depth < MaximumNestingDepth:
                    embeddedReply = ParseSegment(input.ReadBytes(), depth + 1, state);
                    break;
                default:
                    state.UnsupportedFieldCount++;
                    if (fieldNumber == EmbeddedReplyField)
                    {
                        state.IsPartial = true;
                    }

                    input.SkipLastField();
                    break;
            }
        }

        var hasReply = replyMessageId.HasValue ||
            replySequence.HasValue ||
            replyTimestamp.HasValue ||
            replySummary is not null ||
            embeddedReply is not null;
        var isMedia = contentType is (int)QqMessageContentType.Image or
            (int)QqMessageContentType.File or
            (int)QqMessageContentType.Voice or
            (int)QqMessageContentType.Video;
        CountIncompatibleMediaFields(
            contentType,
            videoDuration,
            imageWidth,
            imageHeight,
            videoWidth,
            videoHeight,
            videoPreviewPath,
            filePreviewPath,
            state);
        return new QqMessageSegment
        {
            SegmentId = segmentId,
            RawContentType = contentType,
            Text = text,
            EmojiId = emojiId,
            EmojiText = emojiText,
            Media = isMedia
                ? new QqMediaMetadata
                {
                    RawMediaSubtype = mediaSubtype,
                    FileName = fileName,
                    LocalPath = localCachePath ?? localSendingPath,
                    FileSize = NonNegative(fileSize, state),
                    DurationSeconds = contentType == (int)QqMessageContentType.Video
                        ? NonNegative(videoDuration, state)
                        : null,
                    Width = contentType == (int)QqMessageContentType.Image
                        ? Positive(imageWidth, state)
                        : contentType == (int)QqMessageContentType.Video
                            ? Positive(videoWidth, state)
                            : null,
                    Height = contentType == (int)QqMessageContentType.Image
                        ? Positive(imageHeight, state)
                        : contentType == (int)QqMessageContentType.Video
                            ? Positive(videoHeight, state)
                            : null,
                    FileExtension = fileExtension,
                    PreviewPath = contentType == (int)QqMessageContentType.Video
                        ? videoPreviewPath
                        : contentType == (int)QqMessageContentType.File
                            ? filePreviewPath
                            : null,
                }
                : null,
            Reply = hasReply
                ? new QqReplyReference
                {
                    MessageIdCandidate = replyMessageId,
                    SequenceCandidate = replySequence,
                    OriginalTimestamp = replyTimestamp,
                    Summary = replySummary,
                    EmbeddedContent = embeddedReply,
                }
                : null,
        };
    }

    private static void CountIncompatibleMediaFields(
        int contentType,
        int? videoDuration,
        int? imageWidth,
        int? imageHeight,
        int? videoWidth,
        int? videoHeight,
        string? videoPreviewPath,
        string? filePreviewPath,
        ParseState state)
    {
        if (contentType != (int)QqMessageContentType.Video)
        {
            state.UnsupportedFieldCount += CountPresent(videoDuration, videoWidth, videoHeight) +
                (videoPreviewPath is null ? 0 : 1);
        }

        if (contentType != (int)QqMessageContentType.Image)
        {
            state.UnsupportedFieldCount += CountPresent(imageWidth, imageHeight);
        }

        if (contentType != (int)QqMessageContentType.File && filePreviewPath is not null)
        {
            state.UnsupportedFieldCount++;
        }
    }

    private static int CountPresent(params int?[] values) => values.Count(value => value.HasValue);

    private static long? NonNegative(long? value, ParseState state)
    {
        if (value < 0)
        {
            state.IsPartial = true;
            return null;
        }

        return value;
    }

    private static int? NonNegative(int? value, ParseState state)
    {
        if (value < 0)
        {
            state.IsPartial = true;
            return null;
        }

        return value;
    }

    private static int? Positive(int? value, ParseState state)
    {
        if (value <= 0 && value.HasValue)
        {
            state.IsPartial = true;
            return null;
        }

        return value;
    }

    private static string? ReadUtf8(CodedInputStream input, ParseState state)
    {
        var value = input.ReadBytes();
        try
        {
            return StrictUtf8.GetString(value.Span);
        }
        catch (DecoderFallbackException)
        {
            state.IsPartial = true;
            return null;
        }
    }

    private sealed class ParseState
    {
        public List<QqMessageSegment> Segments { get; } = [];

        public int UnsupportedFieldCount { get; set; }

        public bool IsPartial { get; set; }
    }
}
