namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqDatabaseDiscovery
{
    private const string DatabaseFileName = "nt_msg.db";

    private static readonly string[] CompanionSuffixes =
    [
        "-wal",
        "-shm",
        "-first.material",
        "-last.material",
    ];

    public static IReadOnlyList<QqDatabaseSet> Discover(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);

        var normalizedRoot = Path.GetFullPath(dataRoot);
        if (!Directory.Exists(normalizedRoot))
        {
            throw new DirectoryNotFoundException("The configured QQ data directory does not exist.");
        }

        var results = new List<QqDatabaseSet>();
        foreach (var accountDirectory in Directory.EnumerateDirectories(normalizedRoot))
        {
            var databasePath = Path.Combine(accountDirectory, "nt_qq", "nt_db", DatabaseFileName);
            if (!File.Exists(databasePath))
            {
                continue;
            }

            var accountId = Path.GetFileName(accountDirectory);
            var companions = CompanionSuffixes
                .Select(suffix => databasePath + suffix)
                .Where(File.Exists)
                .ToArray();

            results.Add(new QqDatabaseSet(accountId, databasePath, companions));
        }

        return results
            .OrderBy(result => result.AccountId, StringComparer.Ordinal)
            .ToArray();
    }
}
