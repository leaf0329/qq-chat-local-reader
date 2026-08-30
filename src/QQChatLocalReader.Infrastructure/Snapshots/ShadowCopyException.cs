namespace QQChatLocalReader.Infrastructure.Snapshots;

public sealed class ShadowCopyException : Exception
{
    public ShadowCopyException(string message)
        : base(message)
    {
    }

    public ShadowCopyException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
