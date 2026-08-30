using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;

namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

internal static class PipeMessageProtocol
{
    private const int HeaderSize = sizeof(int);
    private const int MaximumPayloadSize = 64 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = false,
    };

    public static async Task WriteAsync<T>(
        Stream stream,
        T message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, SerializerOptions);
        try
        {
            if (payload.Length is 0 or > MaximumPayloadSize)
            {
                throw new InvalidDataException("The helper message is outside the allowed size.");
            }

            var header = new byte[HeaderSize];
            BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[HeaderSize];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength is <= 0 or > MaximumPayloadSize)
        {
            throw new InvalidDataException("The helper message is outside the allowed size.");
        }

        var payload = new byte[payloadLength];
        try
        {
            await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<T>(payload, SerializerOptions)
                ?? throw new InvalidDataException("The helper message is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The helper message is invalid.", exception);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }
}
