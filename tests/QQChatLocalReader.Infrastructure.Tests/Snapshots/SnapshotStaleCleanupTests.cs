using QQChatLocalReader.Infrastructure.Snapshots;

namespace QQChatLocalReader.Infrastructure.Tests.Snapshots;

public sealed class SnapshotStaleCleanupTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-stale-test-{Guid.NewGuid():N}");

    [Fact]
    public void DeleteOlderThanRemovesOnlyOldGuidSnapshotDirectories()
    {
        Directory.CreateDirectory(testRoot);
        var oldDirectory = CreateSnapshotDirectory(Guid.NewGuid().ToString("N"));
        var recentDirectory = CreateSnapshotDirectory(Guid.NewGuid().ToString("N"));
        var unrelatedDirectory = CreateSnapshotDirectory("unrelated");
        Directory.SetLastWriteTimeUtc(oldDirectory, DateTime.UtcNow.AddDays(-2));

        SnapshotStaleCleanup.DeleteOlderThan(
            testRoot,
            DateTimeOffset.UtcNow.AddDays(-1));

        Assert.False(Directory.Exists(oldDirectory));
        Assert.True(Directory.Exists(recentDirectory));
        Assert.True(Directory.Exists(unrelatedDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private string CreateSnapshotDirectory(string name)
    {
        var path = Path.Combine(testRoot, name);
        Directory.CreateDirectory(path);
        File.WriteAllBytes(Path.Combine(path, "nt_msg.db"), [1]);
        return path;
    }
}
