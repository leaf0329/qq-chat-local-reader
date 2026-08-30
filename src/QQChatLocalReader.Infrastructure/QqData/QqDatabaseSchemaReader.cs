using System.Globalization;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Infrastructure.Secrets;

namespace QQChatLocalReader.Infrastructure.QqData;

public static class QqDatabaseSchemaReader
{
    public static QqDatabaseSchema Read(
        QqPreparedDatabase database,
        QqDatabaseKey key)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(key);

        QqDatabaseSchema? schema = null;
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            schema = Read(connection);
            return true;
        });
        return schema ?? throw new InvalidOperationException("The database schema could not be read.");
    }

    private static QqDatabaseSchema Read(SqliteConnection connection)
    {
        var tableNames = new List<string>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT name
                FROM sqlite_schema
                WHERE type = 'table'
                  AND name NOT LIKE 'sqlite_%'
                ORDER BY name;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                tableNames.Add(reader.GetString(0));
            }
        }

        var tables = tableNames
            .Select(name => new QqTableSchema(name, ReadColumns(connection, name)))
            .ToArray();
        return new QqDatabaseSchema(tables);
    }

    private static List<QqColumnSchema> ReadColumns(
        SqliteConnection connection,
        string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT name, type, "notnull", pk, hidden
            FROM pragma_table_xinfo($tableName)
            ORDER BY cid;
            """;
        command.Parameters.AddWithValue("$tableName", tableName);
        using var reader = command.ExecuteReader();
        var columns = new List<QqColumnSchema>();
        while (reader.Read())
        {
            columns.Add(new QqColumnSchema(
                reader.GetString(0),
                reader.GetString(1),
                Convert.ToBoolean(reader.GetInt64(2), CultureInfo.InvariantCulture),
                reader.GetInt32(3),
                reader.GetInt64(4) != 0));
        }

        return columns;
    }
}
