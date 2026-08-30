namespace QQChatLocalReader.Infrastructure.Snapshots;

internal static class SnapshotDirectoryCleanup
{
    public static void Delete(string snapshotRoot, string directoryPath)
    {
        var normalizedRoot = Path.GetFullPath(snapshotRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedDirectory = Path.GetFullPath(directoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!string.Equals(
                Path.GetDirectoryName(normalizedDirectory),
                normalizedRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Refusing to clean a snapshot outside the configured root.");
        }

        if (!Directory.Exists(normalizedDirectory))
        {
            return;
        }

        var directory = new DirectoryInfo(normalizedDirectory);
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0 ||
            directory.EnumerateDirectories().Any())
        {
            throw new InvalidOperationException("Refusing to recursively clean an unexpected snapshot directory.");
        }

        foreach (var file in directory.EnumerateFiles())
        {
            file.Delete();
        }

        directory.Delete();
    }
}
