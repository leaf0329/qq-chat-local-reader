using System.Globalization;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Secrets;

public static class QqSqlCipherCandidateValidator
{
    private const int RequiredTableCount = 2;

    static QqSqlCipherCandidateValidator()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public static bool IsCandidateValid(
        QqPreparedDatabase database,
        ReadOnlySpan<byte> candidate)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (candidate.Length != AsciiKeyCandidateExtractor.CandidateLength)
        {
            return false;
        }

        try
        {
            using var connection = OpenConfiguredConnection(database.DatabasePath, candidate);
            return HasRequiredSchema(connection);
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    public static void ValidateIntegrity(
        QqPreparedDatabase database,
        ReadOnlySpan<byte> candidate)
    {
        ArgumentNullException.ThrowIfNull(database);
        if (candidate.Length != AsciiKeyCandidateExtractor.CandidateLength)
        {
            throw new ArgumentException("The SQLCipher candidate must be 16 bytes.", nameof(candidate));
        }

        using var connection = OpenConfiguredConnection(database.DatabasePath, candidate);
        if (!HasRequiredSchema(connection))
        {
            throw new InvalidDataException("The required QQ message tables are missing.");
        }

        using (var cipherCheck = connection.CreateCommand())
        {
            cipherCheck.CommandText = "PRAGMA cipher_integrity_check;";
            using var reader = cipherCheck.ExecuteReader();
            if (reader.Read())
            {
                throw new InvalidDataException("SQLCipher page authentication failed.");
            }
        }

        using var quickCheck = connection.CreateCommand();
        quickCheck.CommandText = "PRAGMA quick_check;";
        var result = Convert.ToString(quickCheck.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (!string.Equals(result, "ok", StringComparison.Ordinal))
        {
            throw new InvalidDataException("SQLite integrity validation failed.");
        }
    }

    private static SqliteConnection OpenConfiguredConnection(
        string databasePath,
        ReadOnlySpan<byte> candidate)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
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

    private static bool HasRequiredSchema(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT count(*)
            FROM sqlite_master
            WHERE type = 'table'
              AND name IN ('c2c_msg_table', 'group_msg_table');
            """;
        var result = command.ExecuteScalar();
        return Convert.ToInt32(result, CultureInfo.InvariantCulture) == RequiredTableCount;
    }
}
