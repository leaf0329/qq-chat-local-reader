namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqPreparedDatabase : IAsyncDisposable
{
    private readonly string ownerDirectory;
    private bool disposed;

    internal QqPreparedDatabase(string ownerDirectory, string databasePath)
    {
        this.ownerDirectory = Path.GetFullPath(ownerDirectory);
        DatabasePath = Path.GetFullPath(databasePath);
    }

    public string DatabasePath { get; }

    public ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return ValueTask.CompletedTask;
        }

        var normalizedPath = Path.GetFullPath(DatabasePath);
        if (!string.Equals(
                Path.GetDirectoryName(normalizedPath),
                ownerDirectory,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clean a prepared database outside its snapshot.");
        }

        foreach (var path in new[] { normalizedPath, normalizedPath + "-wal", normalizedPath + "-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        disposed = true;
        return ValueTask.CompletedTask;
    }

    public override string ToString() => $"{nameof(QqPreparedDatabase)} {{ sensitive values omitted }}";
}
