using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Snapshots;

public sealed class VssDatabaseSnapshotProvider
{
    private readonly IShadowCopyService shadowCopyService;
    private readonly string snapshotRoot;

    public VssDatabaseSnapshotProvider(IShadowCopyService shadowCopyService, string? snapshotRoot = null)
    {
        this.shadowCopyService = shadowCopyService ?? throw new ArgumentNullException(nameof(shadowCopyService));
        this.snapshotRoot = Path.GetFullPath(snapshotRoot ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QQChatLocalReader",
            "temp"));
    }

    public async Task<DatabaseSnapshot> CreateAsync(
        QqDatabaseSet databaseSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseSet);

        var databasePath = Path.GetFullPath(databaseSet.DatabasePath);
        var volumeRoot = Path.GetPathRoot(databasePath)
            ?? throw new InvalidOperationException("The database path has no volume root.");
        var sourcePaths = new[] { databasePath }
            .Concat(databaseSet.CompanionPaths.Select(Path.GetFullPath))
            .ToArray();

        if (sourcePaths.Any(path =>
                !string.Equals(Path.GetPathRoot(path), volumeRoot, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("All snapshot files must be on the same volume.");
        }

        Directory.CreateDirectory(snapshotRoot);
        var destinationDirectory = Path.Combine(snapshotRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destinationDirectory);

        try
        {
            await using (var shadowCopy = await shadowCopyService
                .CreateAsync(volumeRoot, cancellationToken)
                .ConfigureAwait(false))
            {
                foreach (var sourcePath in sourcePaths)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var relativePath = Path.GetRelativePath(volumeRoot, sourcePath);
                    var shadowSource = Path.Combine(shadowCopy.DevicePath, relativePath);
                    var destinationPath = Path.Combine(destinationDirectory, Path.GetFileName(sourcePath));

                    await CopyAsync(shadowSource, destinationPath, cancellationToken).ConfigureAwait(false);
                }
            }

            var destinationDatabase = Path.Combine(destinationDirectory, Path.GetFileName(databasePath));
            var destinationCompanions = sourcePaths
                .Skip(1)
                .Select(path => Path.Combine(destinationDirectory, Path.GetFileName(path)))
                .ToArray();

            return new DatabaseSnapshot(
                snapshotRoot,
                destinationDirectory,
                destinationDatabase,
                destinationCompanions);
        }
        catch (Exception snapshotException)
        {
            try
            {
                SnapshotDirectoryCleanup.Delete(snapshotRoot, destinationDirectory);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Snapshot creation failed and its partial files could not be removed.",
                    snapshotException,
                    cleanupException);
            }

            throw;
        }
    }

    private static async Task CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int BufferSize = 1024 * 128;
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await source.CopyToAsync(destination, BufferSize, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
