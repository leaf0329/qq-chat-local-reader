using System.Globalization;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;
using QQChatLocalReader.Infrastructure.Secrets;

namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqNtMessageDatabaseAdapter
{
    public const string SupportedVersion = "9.9.33-52230";

    private static readonly IReadOnlyDictionary<string, string> RequiredCommonColumns =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["40001"] = "INTEGER",
            ["40010"] = "INTEGER",
            ["40013"] = "INTEGER",
            ["40020"] = "TEXT",
            ["40021"] = "TEXT",
            ["40027"] = "INTEGER",
            ["40030"] = "INTEGER",
            ["40033"] = "INTEGER",
            ["40050"] = "INTEGER",
            ["40093"] = "TEXT",
            ["40800"] = "BLOB",
        };

    private readonly string accountId;
    private readonly QqPreparedDatabase database;
    private readonly QqDatabaseKey key;

    private QqNtMessageDatabaseAdapter(
        string accountId,
        QqPreparedDatabase database,
        QqDatabaseKey key)
    {
        this.accountId = accountId;
        this.database = database;
        this.key = key;
    }

    public static QqNtMessageDatabaseAdapter Open(
        string version,
        string accountId,
        QqPreparedDatabase database,
        QqDatabaseKey key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(key);

        if (!version.Equals(SupportedVersion, StringComparison.Ordinal))
        {
            throw new QqAdapterCompatibilityException("The running QQ version is not supported by this adapter.");
        }

        if (!accountId.All(char.IsAsciiDigit))
        {
            throw new QqAdapterCompatibilityException("The QQ account directory has an unsupported identifier.");
        }

        ValidateSchema(QqDatabaseSchemaReader.Read(database, key));
        if (!HasOwnershipEvidence(database, key, accountId))
        {
            throw new QqAdapterCompatibilityException("The message database could not be proven to belong to the selected account.");
        }

        return new QqNtMessageDatabaseAdapter(accountId, database, key);
    }

    public IReadOnlyList<ConversationDescriptor> ListConversations()
    {
        IReadOnlyList<ConversationDescriptor>? conversations = null;
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            conversations = ReadPrivateConversations(connection)
                .Concat(ReadGroupConversations(connection))
                .OrderBy(item => item.Type)
                .ThenBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray();
            return true;
        });
        return conversations ?? throw new InvalidOperationException("The conversation list could not be read.");
    }

    public QqMessageBodyValidationReport ValidateMessageBodies(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.AccountId.Equals(accountId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The sync request belongs to a different QQ account.", nameof(request));
        }

        var accumulator = new MessageBodyValidationAccumulator();
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            foreach (var conversation in request.Conversations)
            {
                ReadMessageBodies(connection, conversation, request.Range, accumulator);
            }

            return true;
        });
        return accumulator.CreateReport();
    }

    private static void ValidateSchema(QqDatabaseSchema schema)
    {
        foreach (var tableName in new[] { "c2c_msg_table", "group_msg_table" })
        {
            var table = schema.Tables.SingleOrDefault(item => item.Name.Equals(tableName, StringComparison.Ordinal));
            if (table is null)
            {
                throw new QqAdapterCompatibilityException("A required QQ message table is missing.");
            }

            var columns = table.Columns.ToDictionary(column => column.Name, StringComparer.Ordinal);
            foreach (var requirement in RequiredCommonColumns)
            {
                if (!columns.TryGetValue(requirement.Key, out var column) ||
                    !column.DeclaredType.Equals(requirement.Value, StringComparison.OrdinalIgnoreCase))
                {
                    throw new QqAdapterCompatibilityException("A required QQ message column is missing or incompatible.");
                }
            }
        }
    }

    private static bool HasOwnershipEvidence(
        QqPreparedDatabase database,
        QqDatabaseKey key,
        string accountId)
    {
        var hasEvidence = false;
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT EXISTS (
                    SELECT 1
                    FROM c2c_msg_table
                    WHERE "40013" IN (1, 2)
                      AND CAST("40033" AS TEXT) = $accountId
                    LIMIT 1
                ) OR EXISTS (
                    SELECT 1
                    FROM group_msg_table
                    WHERE "40013" IN (1, 2)
                      AND CAST("40033" AS TEXT) = $accountId
                    LIMIT 1
                );
                """;
            command.Parameters.AddWithValue("$accountId", accountId);
            hasEvidence = Convert.ToBoolean(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            return true;
        });
        return hasEvidence;
    }

    private List<ConversationDescriptor> ReadPrivateConversations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CAST(peer."40030" AS TEXT),
                   COALESCE((
                       SELECT NULLIF(TRIM(message."40093"), '')
                       FROM c2c_msg_table AS message
                       WHERE message."40030" = peer."40030"
                         AND message."40013" = 0
                         AND NULLIF(TRIM(message."40093"), '') IS NOT NULL
                       ORDER BY message."40050" DESC, message."40001" DESC
                       LIMIT 1
                   ), CAST(peer."40030" AS TEXT))
            FROM c2c_msg_table AS peer
            WHERE peer."40030" > 0
            GROUP BY peer."40030";
            """;
        return ReadConversations(command, ConversationType.Private);
    }

    private List<ConversationDescriptor> ReadGroupConversations(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT CAST("40030" AS TEXT), CAST("40030" AS TEXT)
            FROM group_msg_table
            WHERE "40030" > 0
            GROUP BY "40030";
            """;
        return ReadConversations(command, ConversationType.Group);
    }

    private List<ConversationDescriptor> ReadConversations(
        SqliteCommand command,
        ConversationType type)
    {
        using var reader = command.ExecuteReader();
        var conversations = new List<ConversationDescriptor>();
        while (reader.Read())
        {
            conversations.Add(new ConversationDescriptor(
                accountId,
                type,
                reader.GetString(0),
                reader.GetString(1)));
        }

        return conversations;
    }

    private static void ReadMessageBodies(
        SqliteConnection connection,
        ConversationDescriptor conversation,
        TimeRange range,
        MessageBodyValidationAccumulator accumulator)
    {
        if (!long.TryParse(conversation.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var conversationId) ||
            conversationId <= 0)
        {
            throw new ArgumentException("The conversation has an unsupported identifier.", nameof(conversation));
        }

        var tableName = conversation.Type switch
        {
            ConversationType.Private => "c2c_msg_table",
            ConversationType.Group => "group_msg_table",
            _ => throw new ArgumentOutOfRangeException(nameof(conversation)),
        };
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT "40800"
            FROM {tableName}
            WHERE "40030" = $conversationId
              AND "40050" >= $startTime
              AND "40050" < $endTime
            ORDER BY "40050", "40001";
            """;
        command.Parameters.AddWithValue("$conversationId", conversationId);
        command.Parameters.AddWithValue("$startTime", range.StartUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$endTime", range.EndUtc.ToUnixTimeSeconds());
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0))
            {
                accumulator.AddMissingBody();
            }
            else
            {
                accumulator.Add(QqMessageBodyParser.Parse(reader.GetFieldValue<byte[]>(0)));
            }
        }
    }

    private sealed class MessageBodyValidationAccumulator
    {
        private int messageCount;
        private int missingBodyCount;
        private int completeBodyCount;
        private int partialBodyCount;
        private int malformedBodyCount;
        private int segmentCount;
        private int textSegmentCount;
        private int emojiSegmentCount;
        private int replySegmentCount;
        private int unsupportedFieldCount;

        public void AddMissingBody()
        {
            messageCount++;
            missingBodyCount++;
        }

        public void Add(QqMessageBody body)
        {
            messageCount++;
            switch (body.Status)
            {
                case QqMessageBodyParseStatus.Complete:
                    completeBodyCount++;
                    break;
                case QqMessageBodyParseStatus.Partial:
                    partialBodyCount++;
                    break;
                case QqMessageBodyParseStatus.Malformed:
                    malformedBodyCount++;
                    break;
                default:
                    throw new InvalidOperationException("The message parser returned an unknown status.");
            }

            segmentCount += body.Segments.Count;
            textSegmentCount += body.Segments.Count(segment => segment.ContentType == QqMessageContentType.Text);
            emojiSegmentCount += body.Segments.Count(segment => segment.ContentType == QqMessageContentType.QqFace);
            replySegmentCount += body.Segments.Count(segment => segment.ContentType == QqMessageContentType.Reply);
            unsupportedFieldCount += body.UnsupportedFieldCount;
        }

        public QqMessageBodyValidationReport CreateReport() => new(
            messageCount,
            missingBodyCount,
            completeBodyCount,
            partialBodyCount,
            malformedBodyCount,
            segmentCount,
            textSegmentCount,
            emojiSegmentCount,
            replySegmentCount,
            unsupportedFieldCount);
    }
}
