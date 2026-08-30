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
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
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

        using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
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
