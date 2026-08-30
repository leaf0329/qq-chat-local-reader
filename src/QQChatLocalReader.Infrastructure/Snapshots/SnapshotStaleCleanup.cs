namespace QQChatLocalReader.Infrastructure.Snapshots;

internal static class SnapshotStaleCleanup
{
    public static void DeleteOlderThan(string snapshotRoot, DateTimeOffset thresholdUtc)
    {
        var root = Path.GetFullPath(snapshotRoot);
        if (!Directory.Exists(root))
        {
            return;
        }

        foreach (var directoryPath in Directory.EnumerateDirectories(root))
        {
            var directory = new DirectoryInfo(directoryPath);
            if (!Guid.TryParseExact(directory.Name, "N", out _) ||
                directory.LastWriteTimeUtc >= thresholdUtc.UtcDateTime ||
                (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                continue;
            }

            try
            {
                SnapshotDirectoryCleanup.Delete(root, directory.FullName);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
            }
        }
    }
}
