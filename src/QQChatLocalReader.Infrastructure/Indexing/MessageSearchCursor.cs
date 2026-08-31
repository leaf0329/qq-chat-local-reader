using System.Buffers.Binary;
using System.Text;

namespace QQChatLocalReader.Infrastructure.Indexing;

internal sealed record MessageSearchCursor(
    long Timestamp,
    int ConversationType,
    string ConversationId,
    string MessageId)
{
    private const byte Version = 1;
    private const int MaximumStringBytes = 4096;

    public string Encode()
    {
        var conversationBytes = Encoding.UTF8.GetBytes(ConversationId);
        var messageBytes = Encoding.UTF8.GetBytes(MessageId);
        var payload = new byte[1 + 8 + 4 + 4 + conversationBytes.Length + 4 + messageBytes.Length];
        payload[0] = Version;
        BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, 8), Timestamp);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(9, 4), ConversationType);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(13, 4), conversationBytes.Length);
        conversationBytes.CopyTo(payload.AsSpan(17));
        var messageLengthOffset = 17 + conversationBytes.Length;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(messageLengthOffset, 4), messageBytes.Length);
        messageBytes.CopyTo(payload.AsSpan(messageLengthOffset + 4));
        return Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static MessageSearchCursor Decode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Length > 12_000)
        {
            throw new ArgumentException("The search cursor is too large.", nameof(value));
        }

        byte[] payload;
        try
        {
            var base64 = value.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            payload = Convert.FromBase64String(base64);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The search cursor is invalid.", nameof(value), exception);
        }

        if (payload.Length < 21 || payload[0] != Version)
        {
            throw new ArgumentException("The search cursor has an unsupported format.", nameof(value));
        }

        var conversationLength = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(13, 4));
        if (conversationLength is < 1 or > MaximumStringBytes || payload.Length < 21 + conversationLength)
        {
            throw new ArgumentException("The search cursor is invalid.", nameof(value));
        }

        var messageLengthOffset = 17 + conversationLength;
        var messageLength = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(messageLengthOffset, 4));
        if (messageLength is < 1 or > MaximumStringBytes ||
            payload.Length != messageLengthOffset + 4 + messageLength)
        {
            throw new ArgumentException("The search cursor is invalid.", nameof(value));
        }

        try
        {
            return new MessageSearchCursor(
                BinaryPrimitives.ReadInt64BigEndian(payload.AsSpan(1, 8)),
                BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(9, 4)),
                new UTF8Encoding(false, true).GetString(payload, 17, conversationLength),
                new UTF8Encoding(false, true).GetString(payload, messageLengthOffset + 4, messageLength));
        }
        catch (DecoderFallbackException exception)
        {
            throw new ArgumentException("The search cursor contains invalid text.", nameof(value), exception);
        }
    }
}
