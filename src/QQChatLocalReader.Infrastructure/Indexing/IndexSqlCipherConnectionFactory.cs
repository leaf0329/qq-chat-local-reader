using System.Globalization;
using Microsoft.Data.Sqlite;

namespace QQChatLocalReader.Infrastructure.Indexing;

internal static class IndexSqlCipherConnectionFactory
{
    public static SqliteConnection Open(string databasePath, IndexDatabaseKey key, bool readOnly = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentNullException.ThrowIfNull(key);
        SqlCipherRuntime.Initialize();

        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.GetFullPath(databasePath),
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());

        try
        {
            connection.Open();
            Execute(connection, "PRAGMA cipher_memory_security = ON;");
            key.Use(bytes =>
            {
                var result = SQLitePCL.raw.sqlite3_key(connection.Handle, bytes);
                if (result != SQLitePCL.raw.SQLITE_OK)
                {
                    throw new SqliteException("SQLCipher rejected the index key.", result);
                }

                return true;
            });
            Execute(connection, "PRAGMA cipher_page_size = 4096;");
            Execute(connection, "PRAGMA kdf_iter = 256000;");
            Execute(connection, "PRAGMA cipher_hmac_algorithm = HMAC_SHA512;");
            Execute(connection, "PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA512;");

            if (string.IsNullOrWhiteSpace(Read(connection, "PRAGMA cipher_version;")) ||
                !string.Equals(Read(connection, "PRAGMA cipher_page_size;"), "4096", StringComparison.Ordinal) ||
                !string.Equals(Read(connection, "PRAGMA kdf_iter;"), "256000", StringComparison.Ordinal) ||
                !string.Equals(Read(connection, "PRAGMA cipher_hmac_algorithm;"), "HMAC_SHA512", StringComparison.Ordinal) ||
                !string.Equals(Read(connection, "PRAGMA cipher_kdf_algorithm;"), "PBKDF2_HMAC_SHA512", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The SQLCipher index profile is unavailable.");
            }

            Execute(connection, "PRAGMA trusted_schema = OFF;");
            Execute(connection, "PRAGMA foreign_keys = ON;");
            Execute(connection, "PRAGMA temp_store = MEMORY;");
            Execute(connection, "PRAGMA mmap_size = 0;");
            if (readOnly)
            {
                Execute(connection, "PRAGMA query_only = ON;");
            }
            else
            {
                Execute(connection, "PRAGMA journal_mode = WAL;");
                Execute(connection, "PRAGMA synchronous = FULL;");
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void Execute(SqliteConnection connection, string text)
    {
        using var command = connection.CreateCommand();
        command.CommandText = text;
        command.ExecuteNonQuery();
    }

    private static string? Read(SqliteConnection connection, string text)
    {
        using var command = connection.CreateCommand();
        command.CommandText = text;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
