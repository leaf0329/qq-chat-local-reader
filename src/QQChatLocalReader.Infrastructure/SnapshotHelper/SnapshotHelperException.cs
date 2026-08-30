namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

public sealed class SnapshotHelperException : Exception
{
    public SnapshotHelperException(string message)
        : base(message)
    {
    }

    public SnapshotHelperException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
