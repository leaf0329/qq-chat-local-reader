using System.Security.Cryptography;

namespace QQChatLocalReader.Infrastructure.Indexing;

internal sealed class IndexDatabaseKey : IDisposable
{
    private byte[]? bytes;

    public IndexDatabaseKey(byte[] bytes)
    {
        if (bytes.Length != 32)
        {
            throw new ArgumentException("The index key must be 256 bits.", nameof(bytes));
        }

        this.bytes = bytes;
    }

    ~IndexDatabaseKey() => Clear();

    public bool Use(IndexKeyVisitor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return operation(bytes ?? throw new ObjectDisposedException(nameof(IndexDatabaseKey)));
    }

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"{nameof(IndexDatabaseKey)} {{ sensitive value omitted }}";

    private void Clear()
    {
        var current = Interlocked.Exchange(ref bytes, null);
        if (current is not null)
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }
}

internal delegate bool IndexKeyVisitor(ReadOnlySpan<byte> key);
