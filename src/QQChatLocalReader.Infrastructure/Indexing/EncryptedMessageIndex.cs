using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.Indexing;

public sealed class EncryptedMessageIndex : IDisposable
{
    private const int ApplicationId = 1_363_364_946;
    private const int SchemaVersion = 1;
    private const string DatabaseFileName = "messages.db";
    private readonly string databasePath;
    private IndexDatabaseKey? key;

    private EncryptedMessageIndex(string directoryPath, IndexDatabaseKey key)
    {
        DirectoryPath = directoryPath;
        databasePath = Path.Combine(directoryPath, DatabaseFileName);
        this.key = key;
    }

    public string DirectoryPath { get; }

    public static EncryptedMessageIndex Open(string directoryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        var fullPath = Path.GetFullPath(directoryPath);
        if (File.Exists(Path.Combine(fullPath, DatabaseFileName)) &&
            !File.Exists(Path.Combine(fullPath, WindowsIndexKeyStore.KeyFileName)))
        {
            throw new InvalidDataException("The encrypted message index key is missing.");
        }

        var key = WindowsIndexKeyStore.OpenOrCreate(fullPath);
        var index = new EncryptedMessageIndex(fullPath, key);
        try
        {
            index.InitializeSchema();
            return index;
        }
        catch
        {
            index.Dispose();
            throw;
        }
    }

    public static EncryptedMessageIndex OpenDefault()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }

        return Open(Path.Combine(localApplicationData, "QQChatLocalReader", "index-v1"));
    }

    public int UpsertMessages(IEnumerable<QqMessageRecord> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var records = messages.ToArray();
        foreach (var record in records)
        {
            Validate(record);
        }

        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();
        foreach (var record in records)
        {
            UpsertConversation(connection, transaction, record);
            UpsertMessage(connection, transaction, record);
            ReplaceTextSegments(connection, transaction, record);
            ReplaceReplyTargets(connection, transaction, record);
        }

        transaction.Commit();
        return records.Length;
    }

    public IReadOnlyList<QqMessageRecord> ReadMessages(
        ConversationDescriptor conversation,
        TimeRange range)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentNullException.ThrowIfNull(range);
        using var connection = OpenConnection(readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT message_id, timestamp_utc, direction, sender_id, sender_display_name, body_json
            FROM messages
            WHERE account_id = $accountId
              AND conversation_type = $conversationType
              AND conversation_id = $conversationId
              AND timestamp_utc >= $startTime
              AND timestamp_utc < $endTime
            ORDER BY timestamp_utc, message_id;
            """;
        command.Parameters.AddWithValue("$accountId", conversation.AccountId);
        command.Parameters.AddWithValue("$conversationType", (int)conversation.Type);
        command.Parameters.AddWithValue("$conversationId", conversation.Id);
        command.Parameters.AddWithValue("$startTime", range.StartUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$endTime", range.EndUtc.ToUnixTimeSeconds());

        var replyTargets = ReadReplyTargets(connection, conversation, range);
        var messages = new List<QqMessageRecord>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var messageId = reader.GetString(0);
            messages.Add(new QqMessageRecord
            {
                AccountId = conversation.AccountId,
                ConversationType = conversation.Type,
                ConversationId = conversation.Id,
                ConversationDisplayName = conversation.DisplayName,
                StableMessageId = messageId,
                TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(1)),
                RawDirection = reader.GetInt32(2),
                SenderId = reader.GetString(3),
                SenderDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                Body = reader.IsDBNull(5) ? null : DeserializeBody(reader.GetString(5)),
                ReplyTargetMessageIds = replyTargets.GetValueOrDefault(messageId) ?? [],
            });
        }

        return messages;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref key, null)?.Dispose();
        GC.SuppressFinalize(this);
    }

    public override string ToString() => $"{nameof(EncryptedMessageIndex)} {{ sensitive path omitted }}";

    private void InitializeSchema()
    {
        using var connection = OpenConnection();
        var applicationId = ReadPragmaInt(connection, "PRAGMA application_id;");
        var schemaVersion = ReadPragmaInt(connection, "PRAGMA user_version;");
        if (applicationId != 0 && applicationId != ApplicationId)
        {
            throw new InvalidDataException("The selected database is not a QQ Chat Local Reader index.");
        }

        if (schemaVersion is not 0 and not SchemaVersion)
        {
            throw new InvalidDataException("The message index schema version is not supported.");
        }

        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction,
            """
            CREATE TABLE IF NOT EXISTS conversations (
                account_id TEXT NOT NULL,
                conversation_type INTEGER NOT NULL,
                conversation_id TEXT NOT NULL,
                display_name TEXT NOT NULL,
                PRIMARY KEY (account_id, conversation_type, conversation_id)
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS messages (
                account_id TEXT NOT NULL,
                conversation_type INTEGER NOT NULL,
                conversation_id TEXT NOT NULL,
                message_id TEXT NOT NULL,
                timestamp_utc INTEGER NOT NULL,
                direction INTEGER NOT NULL,
                sender_id TEXT NOT NULL,
                sender_display_name TEXT,
                body_json TEXT,
                PRIMARY KEY (account_id, conversation_type, conversation_id, message_id),
                FOREIGN KEY (account_id, conversation_type, conversation_id)
                    REFERENCES conversations (account_id, conversation_type, conversation_id)
                    ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS messages_by_time
                ON messages (account_id, conversation_type, conversation_id, timestamp_utc, message_id);

            CREATE TABLE IF NOT EXISTS message_text_segments (
                account_id TEXT NOT NULL,
                conversation_type INTEGER NOT NULL,
                conversation_id TEXT NOT NULL,
                message_id TEXT NOT NULL,
                segment_index INTEGER NOT NULL,
                text_content TEXT NOT NULL,
                PRIMARY KEY (account_id, conversation_type, conversation_id, message_id, segment_index),
                FOREIGN KEY (account_id, conversation_type, conversation_id, message_id)
                    REFERENCES messages (account_id, conversation_type, conversation_id, message_id)
                    ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS reply_targets (
                account_id TEXT NOT NULL,
                conversation_type INTEGER NOT NULL,
                conversation_id TEXT NOT NULL,
                message_id TEXT NOT NULL,
                target_message_id TEXT NOT NULL,
                PRIMARY KEY (account_id, conversation_type, conversation_id, message_id, target_message_id),
                FOREIGN KEY (account_id, conversation_type, conversation_id, message_id)
                    REFERENCES messages (account_id, conversation_type, conversation_id, message_id)
                    ON DELETE CASCADE
            ) WITHOUT ROWID;
            """);
        Execute(connection, transaction, $"PRAGMA application_id = {ApplicationId.ToString(CultureInfo.InvariantCulture)};");
        Execute(connection, transaction, $"PRAGMA user_version = {SchemaVersion.ToString(CultureInfo.InvariantCulture)};");
        transaction.Commit();
        VerifyIntegrity(connection);
    }

    private SqliteConnection OpenConnection(bool readOnly = false) =>
        IndexSqlCipherConnectionFactory.Open(
            databasePath,
            key ?? throw new ObjectDisposedException(nameof(EncryptedMessageIndex)),
            readOnly);

    private static void UpsertConversation(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QqMessageRecord record)
    {
        using var command = CreateCommand(connection, transaction,
            """
            INSERT INTO conversations (account_id, conversation_type, conversation_id, display_name)
            VALUES ($accountId, $conversationType, $conversationId, $displayName)
            ON CONFLICT (account_id, conversation_type, conversation_id)
            DO UPDATE SET display_name = excluded.display_name;
            """);
        AddMessageKey(command, record);
        command.Parameters.AddWithValue("$displayName", record.ConversationDisplayName);
        command.ExecuteNonQuery();
    }

    private static void UpsertMessage(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QqMessageRecord record)
    {
        using var command = CreateCommand(connection, transaction,
            """
            INSERT INTO messages (
                account_id, conversation_type, conversation_id, message_id,
                timestamp_utc, direction, sender_id, sender_display_name, body_json)
            VALUES (
                $accountId, $conversationType, $conversationId, $messageId,
                $timestamp, $direction, $senderId, $senderDisplayName, $bodyJson)
            ON CONFLICT (account_id, conversation_type, conversation_id, message_id)
            DO UPDATE SET
                timestamp_utc = excluded.timestamp_utc,
                direction = excluded.direction,
                sender_id = excluded.sender_id,
                sender_display_name = excluded.sender_display_name,
                body_json = excluded.body_json;
            """);
        AddMessageKey(command, record, includeMessageId: true);
        command.Parameters.AddWithValue("$timestamp", record.TimestampUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$direction", record.RawDirection);
        command.Parameters.AddWithValue("$senderId", record.SenderId);
        command.Parameters.AddWithValue("$senderDisplayName", (object?)record.SenderDisplayName ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$bodyJson",
            record.Body is null ? DBNull.Value : JsonSerializer.Serialize(record.Body));
        command.ExecuteNonQuery();
    }

    private static void ReplaceTextSegments(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QqMessageRecord record)
    {
        DeleteChildren(connection, transaction, "message_text_segments", record);
        if (record.Body is null)
        {
            return;
        }

        for (var index = 0; index < record.Body.Segments.Count; index++)
        {
            var text = record.Body.Segments[index].Text;
            if (text is null)
            {
                continue;
            }

            using var command = CreateCommand(connection, transaction,
                """
                INSERT INTO message_text_segments (
                    account_id, conversation_type, conversation_id, message_id, segment_index, text_content)
                VALUES ($accountId, $conversationType, $conversationId, $messageId, $segmentIndex, $text);
                """);
            AddMessageKey(command, record, includeMessageId: true);
            command.Parameters.AddWithValue("$segmentIndex", index);
            command.Parameters.AddWithValue("$text", text);
            command.ExecuteNonQuery();
        }
    }

    private static void ReplaceReplyTargets(
        SqliteConnection connection,
        SqliteTransaction transaction,
        QqMessageRecord record)
    {
        DeleteChildren(connection, transaction, "reply_targets", record);
        foreach (var target in record.ReplyTargetMessageIds.Distinct(StringComparer.Ordinal))
        {
            using var command = CreateCommand(connection, transaction,
                """
                INSERT INTO reply_targets (
                    account_id, conversation_type, conversation_id, message_id, target_message_id)
                VALUES ($accountId, $conversationType, $conversationId, $messageId, $targetMessageId);
                """);
            AddMessageKey(command, record, includeMessageId: true);
            command.Parameters.AddWithValue("$targetMessageId", target);
            command.ExecuteNonQuery();
        }
    }

    private static void DeleteChildren(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tableName,
        QqMessageRecord record)
    {
        var commandText = tableName switch
        {
            "message_text_segments" => "DELETE FROM message_text_segments WHERE account_id = $accountId AND conversation_type = $conversationType AND conversation_id = $conversationId AND message_id = $messageId;",
            "reply_targets" => "DELETE FROM reply_targets WHERE account_id = $accountId AND conversation_type = $conversationType AND conversation_id = $conversationId AND message_id = $messageId;",
            _ => throw new ArgumentOutOfRangeException(nameof(tableName)),
        };
        using var command = CreateCommand(connection, transaction, commandText);
        AddMessageKey(command, record, includeMessageId: true);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, List<string>> ReadReplyTargets(
        SqliteConnection connection,
        ConversationDescriptor conversation,
        TimeRange range)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT target.message_id, target.target_message_id
            FROM reply_targets AS target
            INNER JOIN messages AS message
                ON message.account_id = target.account_id
               AND message.conversation_type = target.conversation_type
               AND message.conversation_id = target.conversation_id
               AND message.message_id = target.message_id
            WHERE target.account_id = $accountId
              AND target.conversation_type = $conversationType
              AND target.conversation_id = $conversationId
              AND message.timestamp_utc >= $startTime
              AND message.timestamp_utc < $endTime
            ORDER BY target.message_id, target.target_message_id;
            """;
        command.Parameters.AddWithValue("$accountId", conversation.AccountId);
        command.Parameters.AddWithValue("$conversationType", (int)conversation.Type);
        command.Parameters.AddWithValue("$conversationId", conversation.Id);
        command.Parameters.AddWithValue("$startTime", range.StartUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$endTime", range.EndUtc.ToUnixTimeSeconds());
        using var reader = command.ExecuteReader();
        var targets = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        while (reader.Read())
        {
            var messageId = reader.GetString(0);
            if (!targets.TryGetValue(messageId, out var existing))
            {
                existing = [];
                targets.Add(messageId, existing);
            }

            existing.Add(reader.GetString(1));
        }

        return targets;
    }

    private static QqMessageBody DeserializeBody(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<QqMessageBody>(json) ??
                throw new InvalidDataException("An indexed message body is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("An indexed message body is invalid.", exception);
        }
    }

    private static void Validate(QqMessageRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.AccountId) ||
            string.IsNullOrWhiteSpace(record.ConversationId) ||
            string.IsNullOrWhiteSpace(record.ConversationDisplayName) ||
            string.IsNullOrWhiteSpace(record.StableMessageId) ||
            string.IsNullOrWhiteSpace(record.SenderId) ||
            !Enum.IsDefined(record.ConversationType))
        {
            throw new ArgumentException("A message record contains an invalid stable identifier.", nameof(record));
        }

        if (record.ReplyTargetMessageIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A message record contains an invalid reply target.", nameof(record));
        }
    }

    private static void AddMessageKey(
        SqliteCommand command,
        QqMessageRecord record,
        bool includeMessageId = false)
    {
        command.Parameters.AddWithValue("$accountId", record.AccountId);
        command.Parameters.AddWithValue("$conversationType", (int)record.ConversationType);
        command.Parameters.AddWithValue("$conversationId", record.ConversationId);
        if (includeMessageId)
        {
            command.Parameters.AddWithValue("$messageId", record.StableMessageId);
        }
    }

    private static SqliteCommand CreateCommand(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string text)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = text;
        return command;
    }

    private static void Execute(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string text)
    {
        using var command = CreateCommand(connection, transaction, text);
        command.ExecuteNonQuery();
    }

    private static int ReadPragmaInt(SqliteConnection connection, string text)
    {
        using var command = connection.CreateCommand();
        command.CommandText = text;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void VerifyIntegrity(SqliteConnection connection)
    {
        using (var cipherCommand = connection.CreateCommand())
        {
            cipherCommand.CommandText = "PRAGMA cipher_integrity_check;";
            using var reader = cipherCommand.ExecuteReader();
            if (reader.Read())
            {
                throw new InvalidDataException("The encrypted message index failed page authentication.");
            }
        }

        using var sqliteCommand = connection.CreateCommand();
        sqliteCommand.CommandText = "PRAGMA quick_check;";
        if (!string.Equals(
                Convert.ToString(sqliteCommand.ExecuteScalar(), CultureInfo.InvariantCulture),
                "ok",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("The encrypted message index failed its integrity check.");
        }
    }
}
