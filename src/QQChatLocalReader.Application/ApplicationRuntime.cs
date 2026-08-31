using QQChatLocalReader.Application.Sync;
using QQChatLocalReader.Infrastructure.Indexing;

namespace QQChatLocalReader.Application;

public sealed class ApplicationRuntime : IDisposable
{
    private bool disposed;

    private ApplicationRuntime(EncryptedMessageIndex index, SyncJobManager syncJobs, ILocalQqCatalog catalog)
    {
        Index = index;
        SyncJobs = syncJobs;
        Catalog = catalog;
    }

    public EncryptedMessageIndex Index { get; }

    public SyncJobManager SyncJobs { get; }

    public ILocalQqCatalog Catalog { get; }

    public static ApplicationRuntime OpenDefault(ISyncRequestAuthorizer? authorizer = null)
    {
        var index = EncryptedMessageIndex.OpenDefault();
        try
        {
            var helperPath = Path.Combine(AppContext.BaseDirectory, "QQChatLocalReader.SnapshotHelper.exe");
            var source = new LocalQqMessageSyncSource(helperPath);
            var jobs = new SyncJobManager(
                source,
                authorizer ?? ExplicitRequestAuthorizer.Instance,
                index);
            return new ApplicationRuntime(index, jobs, source);
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        SyncJobs.Dispose();
        Index.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class ExplicitRequestAuthorizer : ISyncRequestAuthorizer
    {
        public static ExplicitRequestAuthorizer Instance { get; } = new();

        public Task<bool> AuthorizeAsync(Core.Models.SyncRequest request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(request.Conversations.Count > 0);
        }
    }
}
