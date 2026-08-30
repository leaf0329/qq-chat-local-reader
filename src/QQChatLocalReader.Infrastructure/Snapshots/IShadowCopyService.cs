namespace QQChatLocalReader.Infrastructure.Snapshots;

public interface IShadowCopyService
{
    ValueTask<IShadowCopyLease> CreateAsync(
        string volumeRoot,
        CancellationToken cancellationToken = default);
}
