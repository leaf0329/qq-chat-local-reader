namespace QQChatLocalReader.Infrastructure.Snapshots;

public sealed class DatabaseSnapshot : IAsyncDisposable
{
    private readonly string snapshotRoot;
    private bool disposed;

    internal DatabaseSnapshot(
        string snapshotRoot,
        string directoryPath,
        string databasePath,
        IReadOnlyList<string> companionPaths)
    {
        this.snapshotRoot = snapshotRoot;
        DirectoryPath = directoryPath;
        DatabasePath = databasePath;
        CompanionPaths = companionPaths;
    }

    public string DirectoryPath { get; }

    public string DatabasePath { get; }

    public IReadOnlyList<string> CompanionPaths { get; }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        SnapshotDirectoryCleanup.Delete(snapshotRoot, DirectoryPath);
        disposed = true;
        return ValueTask.CompletedTask;
    }

    public override string ToString() => $"{nameof(DatabaseSnapshot)} {{ sensitive values omitted }}";
}
