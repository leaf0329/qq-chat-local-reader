using System.Text;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.Secrets;

namespace QQChatLocalReader.Infrastructure.Tests.QqData;

public sealed class QqGroupInfoDatabaseAdapterTests : IDisposable
{
    private readonly string testRoot = Path.Combine(Path.GetTempPath(), $"qclr-group-info-{Guid.NewGuid():N}");

    [Fact]
    public async Task ReadsVerifiedGroupNamesAndMemberDisplayNames()
    {
        Directory.CreateDirectory(testRoot);
        var path = Path.Combine(testRoot, "prepared.db");
        var bytes = Encoding.ASCII.GetBytes("group-test-key-1");
        CreateDatabase(path, bytes);
        await using var prepared = new QqPreparedDatabase(testRoot, path);
        using var key = new QqDatabaseKey(bytes.ToArray());

        var adapter = QqGroupInfoDatabaseAdapter.Open(QqNtMessageDatabaseAdapter.SupportedVersion, prepared, key);
        var names = adapter.ReadGroupNames();
        var members = adapter.ReadGroupMembers("12345");

        Assert.Equal("合成测试群", names["12345"]);
        var member = Assert.Single(members);
        Assert.Equal("67890", member.MemberId);
        Assert.Equal("合成昵称", member.DisplayName);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot)) Directory.Delete(testRoot, recursive: true);
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
        foreach (var sql in new[]
        {
            "PRAGMA cipher_page_size=4096;",
            "PRAGMA kdf_iter=4000;",
            "PRAGMA cipher_hmac_algorithm=HMAC_SHA1;",
            "PRAGMA cipher_kdf_algorithm=PBKDF2_HMAC_SHA512;",
            "CREATE TABLE group_list (\"60001\" INTEGER, \"60007\" TEXT);",
            "CREATE TABLE group_member3 (\"60001\" INTEGER, \"1002\" INTEGER, \"20002\" TEXT);",
            "CREATE VIRTUAL TABLE group_list_search USING fts5(value);",
            "INSERT INTO group_list VALUES (12345, '合成测试群');",
            "INSERT INTO group_member3 VALUES (12345, 67890, '合成昵称');",
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }
    }
}
