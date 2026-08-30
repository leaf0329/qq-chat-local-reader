namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqAdapterCompatibilityException : Exception
{
    public QqAdapterCompatibilityException(string message)
        : base(message)
    {
    }
}
