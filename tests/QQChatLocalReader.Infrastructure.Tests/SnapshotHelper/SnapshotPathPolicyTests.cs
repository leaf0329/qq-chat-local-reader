using QQChatLocalReader.Infrastructure.SnapshotHelper;

namespace QQChatLocalReader.Infrastructure.Tests.SnapshotHelper;

public sealed class SnapshotPathPolicyTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-helper-policy-{Guid.NewGuid():N}");

    [Fact]
    public void ValidateRequestAcceptsOnlyKnownFilesInsideConfiguredQqRoot()
    {
        var databaseDirectory = Path.Combine(testRoot, "masked-account", "nt_qq", "nt_db");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "nt_msg.db");
        var walPath = databasePath + "-wal";
        File.WriteAllBytes(databasePath, [1]);
        File.WriteAllBytes(walPath, [2]);
        var request = CreateRequest(databasePath, [walPath]);

        var result = SnapshotPathPolicy.ValidateRequest(request, testRoot);

        Assert.Equal(databasePath, result.DatabasePath);
        Assert.Equal([walPath], result.CompanionPaths);
        Assert.DoesNotContain("masked-account", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateRequestRejectsArbitraryCompanionFile()
    {
        var databaseDirectory = Path.Combine(testRoot, "masked-account", "nt_qq", "nt_db");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "nt_msg.db");
        var arbitraryPath = Path.Combine(databaseDirectory, "unrelated.txt");
        File.WriteAllBytes(databasePath, [1]);
        File.WriteAllBytes(arbitraryPath, [2]);

        Assert.Throws<InvalidDataException>(() =>
            SnapshotPathPolicy.ValidateRequest(
                CreateRequest(databasePath, [arbitraryPath]),
                testRoot));
    }

    [Fact]
    public void ValidateRequestRejectsNestedAccountLayout()
    {
        var databaseDirectory = Path.Combine(
            testRoot,
            "nested",
            "masked-account",
            "nt_qq",
            "nt_db");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "nt_msg.db");
        File.WriteAllBytes(databasePath, [1]);

        Assert.Throws<InvalidDataException>(() =>
            SnapshotPathPolicy.ValidateRequest(CreateRequest(databasePath, []), testRoot));
    }

    [Fact]
    public void ValidateRequestAllowsGroupInformationDatabase()
    {
        var databaseDirectory = Path.Combine(testRoot, "masked-account", "nt_qq", "nt_db");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "group_info.db");
        File.WriteAllBytes(databasePath, [1]);

        var result = SnapshotPathPolicy.ValidateRequest(CreateRequest(databasePath, []), testRoot);

        Assert.Equal(databasePath, result.DatabasePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static SnapshotHelperRequest CreateRequest(
        string databasePath,
        string[] companionPaths) => new()
        {
            DatabasePath = databasePath,
            CompanionPaths = companionPaths,
            SnapshotRoot = SnapshotPathPolicy.GetDefaultSnapshotRoot(),
        };
}
