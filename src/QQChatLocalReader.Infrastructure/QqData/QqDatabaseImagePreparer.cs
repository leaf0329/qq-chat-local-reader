using System.Buffers.Binary;
using QQChatLocalReader.Infrastructure.Snapshots;

namespace QQChatLocalReader.Infrastructure.QqData;

public static class QqDatabaseImagePreparer
{
    public const int QqHeaderSize = 1024;
    public const int CipherPageSize = 4096;

    public static async Task<QqPreparedDatabase> PrepareAsync(
        DatabaseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return await PrepareImageAsync(
            snapshot.DirectoryPath,
            snapshot.DatabasePath,
            snapshot.CompanionPaths,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<QqPreparedDatabase> PrepareImageAsync(
        string directoryPath,
        string databasePath,
        IReadOnlyList<string> companionPaths,
        CancellationToken cancellationToken)
    {

        var sourceInfo = new FileInfo(databasePath);
        if (sourceInfo.Length <= QqHeaderSize ||
            (sourceInfo.Length - QqHeaderSize) % CipherPageSize != 0)
        {
            throw new InvalidDataException("The QQ database image has an unexpected page layout.");
        }

        var destinationPath = Path.Combine(
            directoryPath,
            $"prepared-{Guid.NewGuid():N}.db");
        var prepared = new QqPreparedDatabase(directoryPath, destinationPath);

        try
        {
            await CopyWithoutPrefixAsync(
                databasePath,
                destinationPath,
                cancellationToken).ConfigureAwait(false);

            var walPath = companionPaths.FirstOrDefault(path =>
                Path.GetFileName(path).Equals(
                    Path.GetFileName(databasePath) + "-wal",
                    StringComparison.OrdinalIgnoreCase));
            if (walPath is not null)
            {
                await ValidateAndCopyWalAsync(
                    walPath,
                    destinationPath + "-wal",
                    cancellationToken).ConfigureAwait(false);
            }

            return prepared;
        }
        catch (Exception preparationException)
        {
            try
            {
                await prepared.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    "Database preparation failed and its partial files could not be removed.",
                    preparationException,
                    cleanupException);
            }

            throw;
        }
    }

    public static Task<QqPreparedDatabase> PrepareGroupInformationAsync(
        DatabaseSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var databasePath = snapshot.CompanionPaths.SingleOrDefault(path =>
            Path.GetFileName(path).Equals("group_info.db", StringComparison.OrdinalIgnoreCase)) ??
            throw new FileNotFoundException("The QQ group information database is unavailable in the snapshot.");
        var companions = snapshot.CompanionPaths.Where(path =>
            Path.GetFileName(path).StartsWith("group_info.db-", StringComparison.OrdinalIgnoreCase)).ToArray();
        return PrepareImageAsync(snapshot.DirectoryPath, databasePath, companions, cancellationToken);
    }

    private static async Task CopyWithoutPrefixAsync(
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
        source.Position = QqHeaderSize;

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

    private static async Task ValidateAndCopyWalAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        const int WalHeaderSize = 32;
        const int WalFrameHeaderSize = 24;

        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var header = new byte[WalHeaderSize];
        try
        {
            await source.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
            var magic = BinaryPrimitives.ReadUInt32BigEndian(header);
            var pageSize = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
            if ((magic is not 0x377f0682 and not 0x377f0683) ||
                pageSize != CipherPageSize ||
                (source.Length - WalHeaderSize) % (WalFrameHeaderSize + CipherPageSize) != 0)
            {
                throw new InvalidDataException("The QQ WAL image has an unexpected layout.");
            }

            source.Position = 0;
            await using var destination = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Array.Clear(header);
        }
    }
}
