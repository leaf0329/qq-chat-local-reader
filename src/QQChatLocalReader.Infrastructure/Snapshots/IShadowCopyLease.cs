namespace QQChatLocalReader.Infrastructure.Snapshots;

public interface IShadowCopyLease : IAsyncDisposable
{
    string DevicePath { get; }
}
