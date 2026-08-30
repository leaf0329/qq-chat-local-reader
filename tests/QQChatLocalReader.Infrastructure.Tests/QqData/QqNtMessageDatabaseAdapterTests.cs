using System.Text;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.Secrets;

namespace QQChatLocalReader.Infrastructure.Tests.QqData;

public sealed class QqNtMessageDatabaseAdapterTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-adapter-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task SupportedProfileProvesOwnershipAndListsStableConversationIds()
    {
        Directory.CreateDirectory(testRoot);
        var databasePath = Path.Combine(testRoot, "prepared.db");
        var keyBytes = Encoding.ASCII.GetBytes("test-key-1234567");
        CreateDatabase(databasePath, keyBytes);
        await using var database = new QqPreparedDatabase(testRoot, databasePath);
        using var key = new QqDatabaseKey(keyBytes.ToArray());

        var adapter = QqNtMessageDatabaseAdapter.Open(
            QqNtMessageDatabaseAdapter.SupportedVersion,
            "10001",
            database,
            key);
        var conversations = adapter.ListConversations();

        Assert.Collection(
            conversations,
            item =>
            {
                Assert.Equal(ConversationType.Private, item.Type);
                Assert.Equal("20002", item.Id);
                Assert.Equal("Masked peer", item.DisplayName);
                Assert.DoesNotContain("20002", item.ToString(), StringComparison.Ordinal);
            },
            item =>
            {
                Assert.Equal(ConversationType.Group, item.Type);
                Assert.Equal("30003", item.Id);
                Assert.Equal("30003", item.DisplayName);
            });
    }

    [Fact]
    public async Task OpenRejectsAccountWithoutDatabaseEvidence()
    {
        Directory.CreateDirectory(testRoot);
        var databasePath = Path.Combine(testRoot, "mismatch.db");
        var keyBytes = Encoding.ASCII.GetBytes("test-key-1234567");
        CreateDatabase(databasePath, keyBytes);
        await using var database = new QqPreparedDatabase(testRoot, databasePath);
        using var key = new QqDatabaseKey(keyBytes.ToArray());

        Assert.Throws<QqAdapterCompatibilityException>(() =>
            QqNtMessageDatabaseAdapter.Open(
                QqNtMessageDatabaseAdapter.SupportedVersion,
                "99999",
                database,
                key));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static void CreateDatabase(string path, byte[] key)
    {
        SQLitePCL.Batteries_V2.Init();
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        connection.Open();
        Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_key(connection.Handle, key));
        Execute(connection, "PRAGMA cipher_page_size = 4096;");
        Execute(connection, "PRAGMA kdf_iter = 4000;");
        Execute(connection, "PRAGMA cipher_hmac_algorithm = HMAC_SHA1;");
        Execute(connection, "PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA512;");
        Execute(connection, $"CREATE TABLE c2c_msg_table ({RequiredColumns});");
        Execute(connection, $"CREATE TABLE group_msg_table ({RequiredColumns});");
        Execute(
            connection,
            """
            INSERT INTO c2c_msg_table
                ("40001", "40010", "40013", "40020", "40021", "40027", "40030", "40033", "40050", "40093", "40800")
            VALUES
                (1, 1, 0, 'uid-peer', 'uid-peer', 20, 20002, 20002, 1700000000, 'Masked peer', X'00'),
                (2, 1, 1, 'uid-self', 'uid-peer', 20, 20002, 10001, 1700000001, 'Masked self', X'00');
            """);
        Execute(
            connection,
            """
            INSERT INTO group_msg_table
                ("40001", "40010", "40013", "40020", "40021", "40027", "40030", "40033", "40050", "40093", "40800")
            VALUES
                (3, 2, 1, 'uid-self', '30003', 30, 30003, 10001, 1700000002, 'Masked self', X'00');
            """);
    }

    private const string RequiredColumns =
        """
        "40001" INTEGER PRIMARY KEY,
        "40010" INTEGER,
        "40013" INTEGER,
        "40020" TEXT,
        "40021" TEXT,
        "40027" INTEGER,
        "40030" INTEGER,
        "40033" INTEGER,
        "40050" INTEGER,
        "40093" TEXT,
        "40800" BLOB
        """;

    private static void Execute(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
