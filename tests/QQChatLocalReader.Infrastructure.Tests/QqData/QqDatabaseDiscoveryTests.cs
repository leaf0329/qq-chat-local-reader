using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Tests.QqData;

public sealed class QqDatabaseDiscoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), $"qq-reader-test-{Guid.NewGuid():N}");

    [Fact]
    public void DiscoverReturnsOnlyAccountDirectoriesWithMessageDatabase()
    {
        var databaseDirectory = Path.Combine(root, "masked-account", "nt_qq", "nt_db");
        Directory.CreateDirectory(databaseDirectory);
        var databasePath = Path.Combine(databaseDirectory, "nt_msg.db");
        File.WriteAllBytes(databasePath, [1]);
        File.WriteAllBytes(databasePath + "-wal", [2]);
        Directory.CreateDirectory(Path.Combine(root, "not-an-account"));

        var result = Assert.Single(QqDatabaseDiscovery.Discover(root));

        Assert.Equal("masked-account", result.AccountId);
        Assert.Equal(databasePath, result.DatabasePath);
        Assert.Equal([databasePath + "-wal"], result.CompanionPaths);
        Assert.DoesNotContain("masked-account", result.ToString(), StringComparison.Ordinal);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
