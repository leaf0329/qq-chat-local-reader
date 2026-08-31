using System.Text;
using Google.Protobuf;
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

        var report = adapter.ValidateMessageBodies(new SyncRequest(
            "10001",
            conversations,
            new TimeRange(
                DateTimeOffset.FromUnixTimeSeconds(1_699_999_999),
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_003))));
        Assert.Equal(3, report.MessageCount);
        Assert.Equal(3, report.MalformedBodyCount);
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

    [Fact]
    public async Task ReadMessagesResolvesOnlyExactTargetsWithinTheSameConversation()
    {
        Directory.CreateDirectory(testRoot);
        var databasePath = Path.Combine(testRoot, "references.db");
        var keyBytes = Encoding.ASCII.GetBytes("test-key-1234567");
        CreateDatabase(databasePath, keyBytes);
        AddReferenceFixtures(databasePath, keyBytes);
        await using var database = new QqPreparedDatabase(testRoot, databasePath);
        using var key = new QqDatabaseKey(keyBytes.ToArray());
        var adapter = QqNtMessageDatabaseAdapter.Open(
            QqNtMessageDatabaseAdapter.SupportedVersion,
            "10001",
            database,
            key);
        var conversations = adapter.ListConversations();

        var messages = adapter.ReadMessages(new SyncRequest(
            "10001",
            conversations,
            new TimeRange(
                DateTimeOffset.FromUnixTimeSeconds(1_699_999_999),
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_010))));

        var privateReply = Assert.Single(messages, message => message.StableMessageId == "2");
        Assert.Equal(["1"], privateReply.ReplyTargetMessageIds);
        var groupReply = Assert.Single(messages, message => message.StableMessageId == "3");
        Assert.Equal(["4"], groupReply.ReplyTargetMessageIds);
        Assert.DoesNotContain("10001", groupReply.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("30003", groupReply.ToString(), StringComparison.Ordinal);
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
        SqlCipherRuntime.Initialize();
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
        "40003" INTEGER DEFAULT 0,
        "40005" INTEGER DEFAULT 0,
        "40010" INTEGER,
        "40013" INTEGER,
        "40020" TEXT,
        "40021" TEXT,
        "40027" INTEGER,
        "40030" INTEGER,
        "40033" INTEGER,
        "40050" INTEGER,
        "40093" TEXT,
        "40800" BLOB,
        "40850" INTEGER DEFAULT 0
        """;

    private static void AddReferenceFixtures(string path, byte[] key)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
        }.ToString());
        connection.Open();
        Assert.Equal(SQLitePCL.raw.SQLITE_OK, SQLitePCL.raw.sqlite3_key(connection.Handle, key));
        Execute(connection, "PRAGMA cipher_page_size = 4096;");
        Execute(connection, "PRAGMA kdf_iter = 4000;");
        Execute(connection, "PRAGMA cipher_hmac_algorithm = HMAC_SHA1;");
        Execute(connection, "PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA512;");

        UpdateBody(connection, "c2c_msg_table", 2, CreateReplyBody(messageId: 1, sequence: null));
        UpdateBody(connection, "group_msg_table", 3, CreateReplyBody(messageId: null, sequence: 77));
        Execute(connection, "UPDATE group_msg_table SET \"40003\" = 78, \"40850\" = 77 WHERE \"40001\" = 3;");
        Execute(
            connection,
            """
            INSERT INTO group_msg_table
                ("40001", "40003", "40010", "40013", "40020", "40021", "40027", "40030", "40033", "40050", "40093", "40800")
            VALUES
                (4, 77, 2, 0, 'uid-peer', '30003', 30, 30003, 20002, 1700000003, 'Masked peer', X'00');
            """);
    }

    private static byte[] CreateReplyBody(long? messageId, long? sequence)
    {
        var segment = CreateMessage(output =>
        {
            WriteInt32(output, 45002, 7);
            if (messageId.HasValue)
            {
                WriteInt64(output, 47401, messageId.Value);
            }

            if (sequence.HasValue)
            {
                WriteInt64(output, 47402, sequence.Value);
            }
        });
        return CreateMessage(output => WriteBytes(output, 40800, segment));
    }

    private static byte[] CreateMessage(Action<CodedOutputStream> write)
    {
        using var stream = new MemoryStream();
        using (var output = new CodedOutputStream(stream, leaveOpen: true))
        {
            write(output);
            output.Flush();
        }

        return stream.ToArray();
    }

    private static void WriteInt32(CodedOutputStream output, int fieldNumber, int value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteInt32(value);
    }

    private static void WriteInt64(CodedOutputStream output, int fieldNumber, long value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.Varint);
        output.WriteInt64(value);
    }

    private static void WriteBytes(CodedOutputStream output, int fieldNumber, byte[] value)
    {
        output.WriteTag(fieldNumber, WireFormat.WireType.LengthDelimited);
        output.WriteBytes(ByteString.CopyFrom(value));
    }

    private static void UpdateBody(SqliteConnection connection, string table, long messageId, byte[] body)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"UPDATE {table} SET \"40800\" = $body WHERE \"40001\" = $messageId;";
        command.Parameters.AddWithValue("$body", body);
        command.Parameters.AddWithValue("$messageId", messageId);
        command.ExecuteNonQuery();
    }

    private static void Execute(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
