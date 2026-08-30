using System.Security.Cryptography;

namespace QQChatLocalReader.Infrastructure.Secrets;

public sealed class QqDatabaseKey : IDisposable
{
    private byte[]? bytes;

    internal QqDatabaseKey(byte[] bytes)
    {
        this.bytes = bytes;
    }

    ~QqDatabaseKey()
    {
        Clear();
    }

    public bool Use(KeyCandidateVisitor operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        var current = bytes ?? throw new ObjectDisposedException(nameof(QqDatabaseKey));
        return operation(current);
    }

    public void Dispose()
    {
        Clear();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"{nameof(QqDatabaseKey)} {{ sensitive value omitted }}";

    private void Clear()
    {
        var current = Interlocked.Exchange(ref bytes, null);
        if (current is not null)
        {
            CryptographicOperations.ZeroMemory(current);
        }
    }
}
