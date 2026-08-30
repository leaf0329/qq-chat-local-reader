using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.Snapshots;

namespace QQChatLocalReader.Infrastructure.Tests.Snapshots;

public sealed class VssDatabaseSnapshotProviderTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-snapshot-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateAsyncCopiesPointInTimeFilesAndCleansBothLeases()
    {
        var sourceDirectory = Path.Combine(testRoot, "source");
        var databasePath = Path.Combine(sourceDirectory, "nt_msg.db");
        var walPath = databasePath + "-wal";
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(databasePath, [0]);
        File.WriteAllBytes(walPath, [0]);

        var volumeRoot = Path.GetPathRoot(databasePath)!;
        var shadowRoot = Path.Combine(testRoot, "shadow");
        WriteShadowFile(shadowRoot, volumeRoot, databasePath, [1, 2, 3]);
        WriteShadowFile(shadowRoot, volumeRoot, walPath, [4, 5]);

        var shadowService = new FakeShadowCopyService(shadowRoot);
        var snapshotRoot = Path.Combine(testRoot, "snapshots");
        var provider = new VssDatabaseSnapshotProvider(shadowService, snapshotRoot);

        var snapshot = await provider.CreateAsync(new QqDatabaseSet(
            "masked-account",
            databasePath,
            [walPath]));

        Assert.True(shadowService.LeaseDisposed);
        Assert.Equal([1, 2, 3], File.ReadAllBytes(snapshot.DatabasePath));
        Assert.Equal([4, 5], File.ReadAllBytes(Assert.Single(snapshot.CompanionPaths)));

        var snapshotDirectory = snapshot.DirectoryPath;
        await snapshot.DisposeAsync();
        Assert.False(Directory.Exists(snapshotDirectory));
    }

    [Fact]
    public async Task CreateAsyncCleansPartialCopyWhenACompanionIsMissing()
    {
        var sourceDirectory = Path.Combine(testRoot, "missing-source");
        var databasePath = Path.Combine(sourceDirectory, "nt_msg.db");
        var walPath = databasePath + "-wal";
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(databasePath, [0]);
        File.WriteAllBytes(walPath, [0]);

        var volumeRoot = Path.GetPathRoot(databasePath)!;
        var shadowRoot = Path.Combine(testRoot, "missing-shadow");
        WriteShadowFile(shadowRoot, volumeRoot, databasePath, [1]);

        var shadowService = new FakeShadowCopyService(shadowRoot);
        var snapshotRoot = Path.Combine(testRoot, "failed-snapshots");
        var provider = new VssDatabaseSnapshotProvider(shadowService, snapshotRoot);

        await Assert.ThrowsAsync<FileNotFoundException>(() => provider.CreateAsync(
            new QqDatabaseSet("masked-account", databasePath, [walPath])));

        Assert.True(shadowService.LeaseDisposed);
        Assert.Empty(Directory.EnumerateFileSystemEntries(snapshotRoot));
    }

    [Fact]
    public async Task DisposeAsyncRefusesUnexpectedNestedDirectory()
    {
        var sourceDirectory = Path.Combine(testRoot, "nested-source");
        var databasePath = Path.Combine(sourceDirectory, "nt_msg.db");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllBytes(databasePath, [0]);

        var volumeRoot = Path.GetPathRoot(databasePath)!;
        var shadowRoot = Path.Combine(testRoot, "nested-shadow");
        WriteShadowFile(shadowRoot, volumeRoot, databasePath, [1]);

        var provider = new VssDatabaseSnapshotProvider(
            new FakeShadowCopyService(shadowRoot),
            Path.Combine(testRoot, "nested-snapshots"));
        var snapshot = await provider.CreateAsync(
            new QqDatabaseSet("masked-account", databasePath, []));
        var unexpectedDirectory = Path.Combine(snapshot.DirectoryPath, "unexpected");
        Directory.CreateDirectory(unexpectedDirectory);

        await Assert.ThrowsAsync<InvalidOperationException>(async () => await snapshot.DisposeAsync());

        Assert.True(Directory.Exists(unexpectedDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void WriteShadowFile(
        string shadowRoot,
        string volumeRoot,
        string originalPath,
        byte[] contents)
    {
        var shadowPath = Path.Combine(shadowRoot, Path.GetRelativePath(volumeRoot, originalPath));
        Directory.CreateDirectory(Path.GetDirectoryName(shadowPath)!);
        File.WriteAllBytes(shadowPath, contents);
    }

    private sealed class FakeShadowCopyService(string devicePath) : IShadowCopyService
    {
        public bool LeaseDisposed { get; private set; }

        public ValueTask<IShadowCopyLease> CreateAsync(
            string volumeRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(string.IsNullOrWhiteSpace(volumeRoot));
            return ValueTask.FromResult<IShadowCopyLease>(new FakeLease(this, devicePath));
        }

        private sealed class FakeLease(FakeShadowCopyService owner, string path) : IShadowCopyLease
        {
            public string DevicePath { get; } = path;

            public ValueTask DisposeAsync()
            {
                owner.LeaseDisposed = true;
                return ValueTask.CompletedTask;
            }
        }
    }
}
