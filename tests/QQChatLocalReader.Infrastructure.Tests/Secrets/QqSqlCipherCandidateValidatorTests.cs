using System.Text;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.Secrets;
using QQChatLocalReader.Infrastructure.Snapshots;

namespace QQChatLocalReader.Infrastructure.Tests.Secrets;

public sealed class QqSqlCipherCandidateValidatorTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-cipher-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task PreparedImageAcceptsOnlyCorrectKeyAndPassesIntegrityChecks()
    {
        var correctKey = Encoding.ASCII.GetBytes("test-key-1234567");
        var wrongKey = Encoding.ASCII.GetBytes("wrong-key-123456");
        var sourceDirectory = Path.Combine(testRoot, "source");
        Directory.CreateDirectory(sourceDirectory);

        var encryptedPath = Path.Combine(sourceDirectory, "encrypted.db");
        CreateEncryptedDatabase(encryptedPath, correctKey);
        var encryptedBytes = File.ReadAllBytes(encryptedPath);

        var qqDatabasePath = Path.Combine(sourceDirectory, "nt_msg.db");
        await using (var qqDatabase = new FileStream(qqDatabasePath, FileMode.CreateNew))
        {
            await qqDatabase.WriteAsync(new byte[QqDatabaseImagePreparer.QqHeaderSize]);
            await qqDatabase.WriteAsync(encryptedBytes);
        }

        var volumeRoot = Path.GetPathRoot(qqDatabasePath)!;
        var shadowRoot = Path.Combine(testRoot, "shadow");
        WriteShadowFile(shadowRoot, volumeRoot, qqDatabasePath, File.ReadAllBytes(qqDatabasePath));

        var provider = new VssDatabaseSnapshotProvider(
            new FakeShadowCopyService(shadowRoot),
            Path.Combine(testRoot, "snapshots"));
        await using var snapshot = await provider.CreateAsync(
            new QqDatabaseSet("masked-account", qqDatabasePath, []));
        await using var prepared = await QqDatabaseImagePreparer.PrepareAsync(snapshot);

        Assert.Equal(encryptedBytes, File.ReadAllBytes(prepared.DatabasePath));
        Assert.False(QqSqlCipherCandidateValidator.IsCandidateValid(prepared, wrongKey));
        Assert.True(QqSqlCipherCandidateValidator.IsCandidateValid(prepared, correctKey));
        QqSqlCipherCandidateValidator.ValidateIntegrity(prepared, correctKey);

        var resolver = new QqDatabaseKeyResolver(new FakeKeyCandidateScanner(wrongKey, correctKey));
        var resolvedKey = resolver.Resolve(123, prepared);
        Assert.True(resolvedKey.Use(candidate => candidate.SequenceEqual(correctKey)));
        resolvedKey.Dispose();
        Assert.Throws<ObjectDisposedException>(() => resolvedKey.Use(_ => true));
    }

    [Fact]
    public async Task PreparedImageRecoversCommittedWalFrames()
    {
        var correctKey = Encoding.ASCII.GetBytes("test-key-1234567");
        var sourceDirectory = Path.Combine(testRoot, "wal-source");
        Directory.CreateDirectory(sourceDirectory);
        var encryptedPath = Path.Combine(sourceDirectory, "encrypted.db");

        using var writer = OpenNewEncryptedDatabase(encryptedPath, correctKey);
        Execute(writer, "PRAGMA journal_mode = WAL;");
        Execute(writer, "PRAGMA wal_autocheckpoint = 0;");
        Execute(writer, "CREATE TABLE c2c_msg_table(id INTEGER PRIMARY KEY);");
        Execute(writer, "CREATE TABLE group_msg_table(id INTEGER PRIMARY KEY);");
        Execute(writer, "INSERT INTO group_msg_table VALUES (1);");

        var qqDatabasePath = Path.Combine(sourceDirectory, "nt_msg.db");
        await WritePrefixedDatabaseAsync(encryptedPath, qqDatabasePath);
        var sourceWalPath = encryptedPath + "-wal";
        Assert.True(File.Exists(sourceWalPath));

        var qqWalPath = qqDatabasePath + "-wal";
        var volumeRoot = Path.GetPathRoot(qqDatabasePath)!;
        var shadowRoot = Path.Combine(testRoot, "wal-shadow");
        WriteShadowFile(shadowRoot, volumeRoot, qqDatabasePath, File.ReadAllBytes(qqDatabasePath));
        WriteShadowFile(shadowRoot, volumeRoot, qqWalPath, ReadSharedBytes(sourceWalPath));

        var provider = new VssDatabaseSnapshotProvider(
            new FakeShadowCopyService(shadowRoot),
            Path.Combine(testRoot, "wal-snapshots"));
        await using var snapshot = await provider.CreateAsync(
            new QqDatabaseSet("masked-account", qqDatabasePath, [qqWalPath]));
        await using var prepared = await QqDatabaseImagePreparer.PrepareAsync(snapshot);

        Assert.True(File.Exists(prepared.DatabasePath + "-wal"));
        Assert.True(QqSqlCipherCandidateValidator.IsCandidateValid(prepared, correctKey));
        QqSqlCipherCandidateValidator.ValidateIntegrity(prepared, correctKey);
    }

    [Fact]
    public async Task PrepareAsyncRejectsMisalignedDatabaseImage()
    {
        var sourceDirectory = Path.Combine(testRoot, "invalid-source");
        Directory.CreateDirectory(sourceDirectory);
        var qqDatabasePath = Path.Combine(sourceDirectory, "nt_msg.db");
        File.WriteAllBytes(
            qqDatabasePath,
            new byte[QqDatabaseImagePreparer.QqHeaderSize + 1]);

        var volumeRoot = Path.GetPathRoot(qqDatabasePath)!;
        var shadowRoot = Path.Combine(testRoot, "invalid-shadow");
        WriteShadowFile(shadowRoot, volumeRoot, qqDatabasePath, File.ReadAllBytes(qqDatabasePath));
        var provider = new VssDatabaseSnapshotProvider(
            new FakeShadowCopyService(shadowRoot),
            Path.Combine(testRoot, "invalid-snapshots"));
        await using var snapshot = await provider.CreateAsync(
            new QqDatabaseSet("masked-account", qqDatabasePath, []));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            QqDatabaseImagePreparer.PrepareAsync(snapshot));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void CreateEncryptedDatabase(string path, byte[] key)
    {
        using var connection = OpenNewEncryptedDatabase(path, key);
        Execute(connection, "CREATE TABLE c2c_msg_table(id INTEGER PRIMARY KEY);");
        Execute(connection, "CREATE TABLE group_msg_table(id INTEGER PRIMARY KEY);");
    }

    private static SqliteConnection OpenNewEncryptedDatabase(string path, byte[] key)
    {
        SQLitePCL.Batteries_V2.Init();
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        var connection = new SqliteConnection(connectionString);
        connection.Open();
        Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_key(connection.Handle, key));
        Execute(connection, "PRAGMA cipher_page_size = 4096;");
        Execute(connection, "PRAGMA kdf_iter = 4000;");
        Execute(connection, "PRAGMA cipher_hmac_algorithm = HMAC_SHA1;");
        Execute(connection, "PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA512;");
        return connection;
    }

    private static async Task WritePrefixedDatabaseAsync(string sourcePath, string destinationPath)
    {
        await using var destination = new FileStream(destinationPath, FileMode.CreateNew);
        await destination.WriteAsync(new byte[QqDatabaseImagePreparer.QqHeaderSize]);
        await destination.WriteAsync(ReadSharedBytes(sourcePath));
    }

    private static byte[] ReadSharedBytes(string path)
    {
        using var source = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var contents = new byte[checked((int)source.Length)];
        source.ReadExactly(contents);
        return contents;
    }

    private static void Execute(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
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
        public ValueTask<IShadowCopyLease> CreateAsync(
            string volumeRoot,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.False(string.IsNullOrWhiteSpace(volumeRoot));
            return ValueTask.FromResult<IShadowCopyLease>(new FakeLease(devicePath));
        }

        private sealed class FakeLease(string path) : IShadowCopyLease
        {
            public string DevicePath { get; } = path;

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeKeyCandidateScanner(params byte[][] candidates) : IKeyCandidateScanner
    {
        public ProcessMemoryScanResult Scan(
            int processId,
            KeyCandidateVisitor visitor,
            CancellationToken cancellationToken = default)
        {
            Assert.True(processId > 0);
            var visited = 0;
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                visited++;
                if (visitor(candidate))
                {
                    return new ProcessMemoryScanResult(visited, MatchFound: true);
                }
            }

            return new ProcessMemoryScanResult(visited, MatchFound: false);
        }
    }
}
