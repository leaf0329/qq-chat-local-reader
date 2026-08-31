using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

internal static class SnapshotPathPolicy
{
    private static readonly string[] AllowedDatabaseNames = ["nt_msg.db", "group_info.db"];
    private static readonly string[] AllowedCompanionSuffixes =
    [
        "-wal",
        "-shm",
        "-first.material",
        "-last.material",
    ];

    public static string GetDefaultSnapshotRoot() => Path.GetFullPath(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "QQChatLocalReader",
        "temp"));

    public static async Task<QqDatabaseSet> ValidateRequestAsync(
        SnapshotHelperRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var configuredRoot = await QqUserDataConfiguration
            .ReadDataRootAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        return ValidateRequest(request, configuredRoot);
    }

    internal static QqDatabaseSet ValidateRequest(
        SnapshotHelperRequest request,
        string configuredRoot)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(configuredRoot);

        var expectedSnapshotRoot = GetDefaultSnapshotRoot();
        if (!string.Equals(
                Path.GetFullPath(request.SnapshotRoot),
                expectedSnapshotRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The snapshot destination is not allowed.");
        }

        var databasePath = Path.GetFullPath(request.DatabasePath);
        var databaseDirectory = Directory.GetParent(databasePath);
        var ntQqDirectory = databaseDirectory?.Parent;
        var accountDirectory = ntQqDirectory?.Parent;
        if (!AllowedDatabaseNames.Contains(Path.GetFileName(databasePath), StringComparer.OrdinalIgnoreCase) ||
            databaseDirectory is null ||
            ntQqDirectory is null ||
            accountDirectory is null ||
            !databaseDirectory.Name.Equals("nt_db", StringComparison.OrdinalIgnoreCase) ||
            !ntQqDirectory.Name.Equals("nt_qq", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The source is not an allowed QQ database.");
        }

        var normalizedConfiguredRoot = Path.GetFullPath(configuredRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(
                accountDirectory.Parent?.FullName.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar),
                normalizedConfiguredRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(databasePath) ||
            HasReparsePoint(accountDirectory, ntQqDirectory, databaseDirectory, new FileInfo(databasePath)))
        {
            throw new InvalidDataException("The source database is outside the configured QQ data directory.");
        }

        if (request.CompanionPaths is null ||
            request.CompanionPaths.Length > (AllowedCompanionSuffixes.Length * 2) + 1)
        {
            throw new InvalidDataException("Too many QQ companion files were requested.");
        }

        var companions = request.CompanionPaths
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var companionPath in companions)
        {
            var groupInfoPath = Path.Combine(databaseDirectory.FullName, "group_info.db");
            var isPrimaryCompanion = AllowedCompanionSuffixes.Any(suffix =>
                companionPath.Equals(databasePath + suffix, StringComparison.OrdinalIgnoreCase));
            var isGroupInformation = Path.GetFileName(databasePath).Equals("nt_msg.db", StringComparison.OrdinalIgnoreCase) &&
                (companionPath.Equals(groupInfoPath, StringComparison.OrdinalIgnoreCase) ||
                 AllowedCompanionSuffixes.Any(suffix => companionPath.Equals(groupInfoPath + suffix, StringComparison.OrdinalIgnoreCase)));
            if ((!isPrimaryCompanion && !isGroupInformation) ||
                !File.Exists(companionPath) ||
                HasReparsePoint(new FileInfo(companionPath)))
            {
                throw new InvalidDataException("An invalid QQ companion file was requested.");
            }
        }

        return new QqDatabaseSet(
            accountDirectory.Name,
            databasePath,
            companions);
    }

    private static bool HasReparsePoint(params FileSystemInfo[] entries) => entries.Any(
        entry => (entry.Attributes & FileAttributes.ReparsePoint) != 0);
}
