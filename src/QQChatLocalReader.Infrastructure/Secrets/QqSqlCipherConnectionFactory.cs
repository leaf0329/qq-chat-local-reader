using System.Globalization;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Secrets;

internal static class QqSqlCipherConnectionFactory
{
    public static SqliteConnection Open(
        QqPreparedDatabase database,
        ReadOnlySpan<byte> candidate)
    {
        ArgumentNullException.ThrowIfNull(database);

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = database.DatabasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString();
        var connection = new SqliteConnection(connectionString);

        try
        {
            connection.Open();
            ExecutePragma(connection, "PRAGMA cipher_memory_security = ON;");

            var result = SQLitePCL.raw.sqlite3_key(connection.Handle, candidate);
            if (result != SQLitePCL.raw.SQLITE_OK)
            {
                throw new SqliteException("SQLCipher rejected the candidate key.", result);
            }

            ExecutePragma(connection, "PRAGMA cipher_page_size = 4096;");
            ExecutePragma(connection, "PRAGMA kdf_iter = 4000;");
            ExecutePragma(connection, "PRAGMA cipher_hmac_algorithm = HMAC_SHA1;");
            ExecutePragma(connection, "PRAGMA cipher_kdf_algorithm = PBKDF2_HMAC_SHA512;");
            VerifyCipherProfile(connection);
            ExecutePragma(connection, "PRAGMA query_only = ON;");
            ExecutePragma(connection, "PRAGMA trusted_schema = OFF;");
            ExecutePragma(connection, "PRAGMA temp_store = MEMORY;");
            ExecutePragma(connection, "PRAGMA mmap_size = 0;");

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static void ExecutePragma(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void VerifyCipherProfile(SqliteConnection connection)
    {
        if (string.IsNullOrWhiteSpace(ReadPragma(connection, "PRAGMA cipher_version;")) ||
            !string.Equals(ReadPragma(connection, "PRAGMA cipher_page_size;"), "4096", StringComparison.Ordinal) ||
            !string.Equals(ReadPragma(connection, "PRAGMA kdf_iter;"), "4000", StringComparison.Ordinal) ||
            !string.Equals(ReadPragma(connection, "PRAGMA cipher_hmac_algorithm;"), "HMAC_SHA1", StringComparison.Ordinal) ||
            !string.Equals(ReadPragma(connection, "PRAGMA cipher_kdf_algorithm;"), "PBKDF2_HMAC_SHA512", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The SQLCipher runtime did not apply the required QQ profile.");
        }
    }

    private static string? ReadPragma(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }
}
