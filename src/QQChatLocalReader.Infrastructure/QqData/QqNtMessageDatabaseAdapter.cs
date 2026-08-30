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
            ["40003"] = "INTEGER",
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
            ["40850"] = "INTEGER",
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
        var accumulator = new MessageBodyValidationAccumulator();
        foreach (var message in ReadMessages(request))
        {
            if (message.Body is null)
            {
                accumulator.AddMissingBody();
            }
            else
            {
                accumulator.Add(message.Body);
            }
        }

        return accumulator.CreateReport();
    }

    public IReadOnlyList<QqMessageRecord> ReadMessages(SyncRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.AccountId.Equals(accountId, StringComparison.Ordinal))
        {
            throw new ArgumentException("The sync request belongs to a different QQ account.", nameof(request));
        }

        List<RawMessage>? messages = null;
        key.Use(candidate =>
        {
            using var connection = QqSqlCipherConnectionFactory.Open(database, candidate);
            messages = request.Conversations
                .SelectMany(conversation => ReadRawMessages(connection, conversation, request.Range))
                .ToList();
            return true;
        });

        return ResolveMessages(messages ?? throw new InvalidOperationException("The messages could not be read."));
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

    private static List<RawMessage> ReadRawMessages(
        SqliteConnection connection,
        ConversationDescriptor conversation,
        TimeRange range)
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
            SELECT "40001", "40003", "40013", "40033", "40050", "40093", "40800", "40850"
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
        var messages = new List<RawMessage>();
        while (reader.Read())
        {
            messages.Add(new RawMessage
            {
                Conversation = conversation,
                StableMessageId = reader.GetInt64(0),
                Sequence = reader.IsDBNull(1) ? null : reader.GetInt64(1),
                Direction = reader.GetInt32(2),
                SenderId = reader.GetInt64(3),
                Timestamp = reader.GetInt64(4),
                SenderDisplayName = reader.IsDBNull(5) ? null : reader.GetString(5),
                Body = reader.IsDBNull(6)
                    ? null
                    : QqMessageBodyParser.Parse(reader.GetFieldValue<byte[]>(6)),
                MainReplySequence = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            });
        }

        return messages;
    }

    private QqMessageRecord[] ResolveMessages(IReadOnlyList<RawMessage> messages)
    {
        var indexes = messages
            .GroupBy(message => message.Conversation.StableKey)
            .ToDictionary(group => group.Key, group => new ConversationMessageIndex(group), StringComparer.Ordinal);

        return messages
            .OrderBy(message => message.Timestamp)
            .ThenBy(message => message.Conversation.StableKey, StringComparer.Ordinal)
            .ThenBy(message => message.StableMessageId)
            .Select(message =>
        {
            var index = indexes[message.Conversation.StableKey];
            var targets = new HashSet<long>();
            if (message.Conversation.Type == ConversationType.Group)
            {
                AddTarget(index.ResolveSequence(message.MainReplySequence), targets);
                foreach (var reference in GetReplyReferences(message.Body))
                {
                    AddTarget(index.ResolveSequence(reference.SequenceCandidate), targets);
                }
            }
            else
            {
                foreach (var reference in GetReplyReferences(message.Body))
                {
                    AddTarget(index.ResolveMessageId(reference.MessageIdCandidate), targets);
                }
            }

            return new QqMessageRecord
            {
                AccountId = accountId,
                ConversationType = message.Conversation.Type,
                ConversationId = message.Conversation.Id,
                StableMessageId = message.StableMessageId.ToString(CultureInfo.InvariantCulture),
                TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(message.Timestamp),
                RawDirection = message.Direction,
                SenderId = message.SenderId.ToString(CultureInfo.InvariantCulture),
                SenderDisplayName = message.SenderDisplayName,
                Body = message.Body,
                ReplyTargetMessageIds = targets
                    .Order()
                    .Select(target => target.ToString(CultureInfo.InvariantCulture))
                    .ToArray(),
            };
        }).ToArray();
    }

    private static IEnumerable<QqReplyReference> GetReplyReferences(QqMessageBody? body) =>
        body?.Segments
            .Where(segment => segment.Reply is not null)
            .Select(segment => segment.Reply!) ?? [];

    private static void AddTarget(long? target, HashSet<long> targets)
    {
        if (target.HasValue)
        {
            targets.Add(target.Value);
        }
    }

    private sealed class ConversationMessageIndex
    {
        private readonly Dictionary<long, long?> messageIds;
        private readonly Dictionary<long, long?> sequences;

        public ConversationMessageIndex(IEnumerable<RawMessage> messages)
        {
            var records = messages.ToArray();
            messageIds = Build(records, message => message.StableMessageId);
            sequences = Build(records, message => message.Sequence);
        }

        public long? ResolveMessageId(long? candidate) => Resolve(messageIds, candidate);

        public long? ResolveSequence(long? candidate) => Resolve(sequences, candidate);

        private static Dictionary<long, long?> Build(
            IEnumerable<RawMessage> messages,
            Func<RawMessage, long?> candidateSelector)
        {
            var index = new Dictionary<long, long?>();
            foreach (var message in messages)
            {
                var candidate = candidateSelector(message);
                if (candidate is not > 0)
                {
                    continue;
                }

                if (!index.TryAdd(candidate.Value, message.StableMessageId))
                {
                    index[candidate.Value] = null;
                }
            }

            return index;
        }

        private static long? Resolve(Dictionary<long, long?> index, long? candidate)
        {
            return candidate is > 0 && index.TryGetValue(candidate.Value, out var target)
                ? target
                : null;
        }
    }

    private sealed class RawMessage
    {
        public required ConversationDescriptor Conversation { get; init; }

        public required long StableMessageId { get; init; }

        public long? Sequence { get; init; }

        public required int Direction { get; init; }

        public required long SenderId { get; init; }

        public required long Timestamp { get; init; }

        public string? SenderDisplayName { get; init; }

        public QqMessageBody? Body { get; init; }

        public long? MainReplySequence { get; init; }
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
