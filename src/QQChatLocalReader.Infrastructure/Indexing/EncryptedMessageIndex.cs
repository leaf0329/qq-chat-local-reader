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
    private const int SchemaVersion = 2;
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
        return Open(GetDefaultDirectoryPath());
    }

    public static void DeleteDefault()
    {
        var directoryPath = GetDefaultDirectoryPath();
        if (Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }

    private static string GetDefaultDirectoryPath()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException("The local application data directory is unavailable.");
        }

        var applicationRoot = Path.GetFullPath(Path.Combine(localApplicationData, "QQChatLocalReader"));
        var directoryPath = Path.GetFullPath(Path.Combine(applicationRoot, "index-v1"));
        if (!directoryPath.StartsWith(
                applicationRoot + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The default index directory is outside the application data directory.");
        }

        return directoryPath;
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

    public void SaveSyncJob(IndexSyncJobRecord job)
    {
        ArgumentNullException.ThrowIfNull(job);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO sync_jobs (
                job_id, state, created_utc, updated_utc, message_count, error_code, request_json)
            VALUES ($jobId, $state, $created, $updated, $count, $error, $request)
            ON CONFLICT (job_id) DO UPDATE SET
                state = excluded.state,
                updated_utc = excluded.updated_utc,
                message_count = excluded.message_count,
                error_code = excluded.error_code,
                request_json = excluded.request_json;
            """;
        command.Parameters.AddWithValue("$jobId", job.JobId.ToString("D"));
        command.Parameters.AddWithValue("$state", job.State);
        command.Parameters.AddWithValue("$created", job.CreatedUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$updated", job.UpdatedUtc.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$count", (object?)job.MessageCount ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)job.ErrorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$request", job.RequestJson);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<IndexSyncJobRecord> ReadSyncJobs()
    {
        using var connection = OpenConnection(readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT job_id, state, created_utc, updated_utc, message_count, error_code, request_json
            FROM sync_jobs
            ORDER BY created_utc, job_id;
            """;
        using var reader = command.ExecuteReader();
        var jobs = new List<IndexSyncJobRecord>();
        while (reader.Read())
        {
            jobs.Add(new IndexSyncJobRecord(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt32(1),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2)),
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3)),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.GetString(6)));
        }

        return jobs;
    }

    public IReadOnlyList<ConversationDescriptor> ListConversations(string? accountId = null)
    {
        using var connection = OpenConnection(readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT account_id, conversation_type, conversation_id, display_name
            FROM conversations
            WHERE $accountId IS NULL OR account_id = $accountId
            ORDER BY account_id, conversation_type, display_name, conversation_id;
            """;
        command.Parameters.AddWithValue("$accountId", (object?)accountId ?? DBNull.Value);
        using var reader = command.ExecuteReader();
        var conversations = new List<ConversationDescriptor>();
        while (reader.Read())
        {
            conversations.Add(new ConversationDescriptor(
                reader.GetString(0),
                (ConversationType)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3)));
        }

        return conversations;
    }

    public MessageIndexStatus GetStatus()
    {
        using var connection = OpenConnection(readOnly: true);
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT conversation.account_id, conversation.conversation_type,
                   conversation.conversation_id, conversation.display_name,
                   count(message.message_id), min(message.timestamp_utc), max(message.timestamp_utc)
            FROM conversations AS conversation
            LEFT JOIN messages AS message
              ON message.account_id = conversation.account_id
             AND message.conversation_type = conversation.conversation_type
             AND message.conversation_id = conversation.conversation_id
            GROUP BY conversation.account_id, conversation.conversation_type,
                     conversation.conversation_id, conversation.display_name
            ORDER BY conversation.account_id, conversation.conversation_type, conversation.conversation_id;
            """;
        using var reader = command.ExecuteReader();
        var rows = new List<ConversationIndexCoverage>();
        var total = 0;
        while (reader.Read())
        {
            var count = reader.GetInt32(4);
            total += count;
            rows.Add(new ConversationIndexCoverage(
                reader.GetString(0),
                (ConversationType)reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                count,
                reader.IsDBNull(5) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(5)),
                reader.IsDBNull(6) ? null : DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(6))));
        }

        return new MessageIndexStatus(total, rows);
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

    public MessageSearchPage SearchMessages(MessageSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var cursor = request.Cursor is null ? null : MessageSearchCursor.Decode(request.Cursor);
        using var connection = OpenConnection(readOnly: true);
        using var command = connection.CreateCommand();
        var conversationPredicates = new List<string>();
        for (var index = 0; index < request.Conversations.Count; index++)
        {
            conversationPredicates.Add($"(message.conversation_type = $type{index} AND message.conversation_id = $id{index})");
            command.Parameters.AddWithValue($"$type{index}", (int)request.Conversations[index].Type);
            command.Parameters.AddWithValue($"$id{index}", request.Conversations[index].Id);
        }

        var cursorPredicate = cursor is null
            ? string.Empty
            :
                """
                  AND (
                        message.timestamp_utc > $cursorTime
                     OR (message.timestamp_utc = $cursorTime AND message.conversation_type > $cursorType)
                     OR (message.timestamp_utc = $cursorTime AND message.conversation_type = $cursorType AND message.conversation_id > $cursorConversation)
                     OR (message.timestamp_utc = $cursorTime AND message.conversation_type = $cursorType AND message.conversation_id = $cursorConversation AND message.message_id > $cursorMessage)
                  )
                """;
        var keywordPredicate = request.Keyword is null
            ? string.Empty
            :
                """
                  AND EXISTS (
                      SELECT 1
                      FROM message_text_segments AS segment
                      WHERE segment.account_id = message.account_id
                        AND segment.conversation_type = message.conversation_type
                        AND segment.conversation_id = message.conversation_id
                        AND segment.message_id = message.message_id
                        AND instr(lower(segment.text_content), lower($keyword)) > 0
                  )
                """;
        var senderPredicate = request.SenderId is null
            ? string.Empty
            : " AND message.sender_id = $senderId";
        command.CommandText =
            $"""
            SELECT message.message_id, message.timestamp_utc, message.direction,
                   message.sender_id, message.sender_display_name, message.body_json,
                   message.conversation_type, message.conversation_id, conversation.display_name
            FROM messages AS message
            INNER JOIN conversations AS conversation
                ON conversation.account_id = message.account_id
               AND conversation.conversation_type = message.conversation_type
               AND conversation.conversation_id = message.conversation_id
            WHERE message.account_id = $accountId
              AND ({string.Join(" OR ", conversationPredicates)})
              AND message.timestamp_utc >= $startTime
              AND message.timestamp_utc < $endTime
              {senderPredicate}
              {keywordPredicate}
              {cursorPredicate}
            ORDER BY message.timestamp_utc, message.conversation_type,
                     message.conversation_id, message.message_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$accountId", request.AccountId);
        command.Parameters.AddWithValue("$startTime", request.Range.StartUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$endTime", request.Range.EndUtc.ToUnixTimeSeconds());
        command.Parameters.AddWithValue("$limit", request.PageSize + 1);
        if (request.Keyword is not null)
        {
            command.Parameters.AddWithValue("$keyword", request.Keyword);
        }

        if (request.SenderId is not null)
        {
            command.Parameters.AddWithValue("$senderId", request.SenderId);
        }

        if (cursor is not null)
        {
            command.Parameters.AddWithValue("$cursorTime", cursor.Timestamp);
            command.Parameters.AddWithValue("$cursorType", cursor.ConversationType);
            command.Parameters.AddWithValue("$cursorConversation", cursor.ConversationId);
            command.Parameters.AddWithValue("$cursorMessage", cursor.MessageId);
        }

        var rows = ReadRows(command);
        var hasNextPage = rows.Count > request.PageSize;
        if (hasNextPage)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        var messages = rows.Select(row => Materialize(connection, request.AccountId, row)).ToArray();
        var last = hasNextPage ? rows[^1] : null;
        return new MessageSearchPage(
            messages,
            last is null
                ? null
                : new MessageSearchCursor(
                    last.Timestamp,
                    last.ConversationType,
                    last.ConversationId,
                    last.MessageId).Encode());
    }

    public MessageContext ReadContext(
        ConversationDescriptor conversation,
        string messageId,
        int before = 20,
        int after = 20)
    {
        ArgumentNullException.ThrowIfNull(conversation);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);
        if (before is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(before));
        }

        if (after is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(after));
        }

        using var connection = OpenConnection(readOnly: true);
        var anchor = ReadAnchor(connection, conversation, messageId) ??
            throw new KeyNotFoundException("The requested indexed message was not found.");
        var previous = ReadContextRows(connection, conversation, anchor, before, beforeAnchor: true);
        previous.Reverse();
        var following = ReadContextRows(connection, conversation, anchor, after, beforeAnchor: false);
        var rows = previous.Append(anchor).Concat(following).ToArray();
        return new MessageContext(
            rows.Select(row => Materialize(connection, conversation.AccountId, row)).ToArray(),
            previous.Count);
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

        if (schemaVersion < 0 || schemaVersion > SchemaVersion)
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

            CREATE TABLE IF NOT EXISTS sync_jobs (
                job_id TEXT PRIMARY KEY,
                state INTEGER NOT NULL,
                created_utc INTEGER NOT NULL,
                updated_utc INTEGER NOT NULL,
                message_count INTEGER,
                error_code TEXT,
                request_json TEXT NOT NULL
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

    private static IndexedRow? ReadAnchor(
        SqliteConnection connection,
        ConversationDescriptor conversation,
        string messageId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT message.message_id, message.timestamp_utc, message.direction,
                   message.sender_id, message.sender_display_name, message.body_json,
                   message.conversation_type, message.conversation_id, conversation.display_name
            FROM messages AS message
            INNER JOIN conversations AS conversation
                ON conversation.account_id = message.account_id
               AND conversation.conversation_type = message.conversation_type
               AND conversation.conversation_id = message.conversation_id
            WHERE message.account_id = $accountId
              AND message.conversation_type = $conversationType
              AND message.conversation_id = $conversationId
              AND message.message_id = $messageId;
            """;
        command.Parameters.AddWithValue("$accountId", conversation.AccountId);
        command.Parameters.AddWithValue("$conversationType", (int)conversation.Type);
        command.Parameters.AddWithValue("$conversationId", conversation.Id);
        command.Parameters.AddWithValue("$messageId", messageId);
        return ReadRows(command).SingleOrDefault();
    }

    private static List<IndexedRow> ReadContextRows(
        SqliteConnection connection,
        ConversationDescriptor conversation,
        IndexedRow anchor,
        int limit,
        bool beforeAnchor)
    {
        if (limit == 0)
        {
            return [];
        }

        var comparison = beforeAnchor ? "<" : ">";
        var order = beforeAnchor ? "DESC" : "ASC";
        using var command = connection.CreateCommand();
        command.CommandText =
            $"""
            SELECT message.message_id, message.timestamp_utc, message.direction,
                   message.sender_id, message.sender_display_name, message.body_json,
                   message.conversation_type, message.conversation_id, conversation.display_name
            FROM messages AS message
            INNER JOIN conversations AS conversation
                ON conversation.account_id = message.account_id
               AND conversation.conversation_type = message.conversation_type
               AND conversation.conversation_id = message.conversation_id
            WHERE message.account_id = $accountId
              AND message.conversation_type = $conversationType
              AND message.conversation_id = $conversationId
              AND (
                    message.timestamp_utc {comparison} $anchorTime
                 OR (message.timestamp_utc = $anchorTime AND message.message_id {comparison} $anchorMessage)
              )
            ORDER BY message.timestamp_utc {order}, message.message_id {order}
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$accountId", conversation.AccountId);
        command.Parameters.AddWithValue("$conversationType", (int)conversation.Type);
        command.Parameters.AddWithValue("$conversationId", conversation.Id);
        command.Parameters.AddWithValue("$anchorTime", anchor.Timestamp);
        command.Parameters.AddWithValue("$anchorMessage", anchor.MessageId);
        command.Parameters.AddWithValue("$limit", limit);
        return ReadRows(command);
    }

    private static List<IndexedRow> ReadRows(SqliteCommand command)
    {
        using var reader = command.ExecuteReader();
        var rows = new List<IndexedRow>();
        while (reader.Read())
        {
            rows.Add(new IndexedRow
            {
                MessageId = reader.GetString(0),
                Timestamp = reader.GetInt64(1),
                Direction = reader.GetInt32(2),
                SenderId = reader.GetString(3),
                SenderDisplayName = reader.IsDBNull(4) ? null : reader.GetString(4),
                BodyJson = reader.IsDBNull(5) ? null : reader.GetString(5),
                ConversationType = reader.GetInt32(6),
                ConversationId = reader.GetString(7),
                ConversationDisplayName = reader.GetString(8),
            });
        }

        return rows;
    }

    private static QqMessageRecord Materialize(
        SqliteConnection connection,
        string accountId,
        IndexedRow row)
    {
        var conversation = new ConversationDescriptor(
            accountId,
            (ConversationType)row.ConversationType,
            row.ConversationId,
            row.ConversationDisplayName);
        return new QqMessageRecord
        {
            AccountId = accountId,
            ConversationType = conversation.Type,
            ConversationId = conversation.Id,
            ConversationDisplayName = conversation.DisplayName,
            StableMessageId = row.MessageId,
            TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(row.Timestamp),
            RawDirection = row.Direction,
            SenderId = row.SenderId,
            SenderDisplayName = row.SenderDisplayName,
            Body = row.BodyJson is null ? null : DeserializeBody(row.BodyJson),
            ReplyTargetMessageIds = ReadReplyTargets(connection, conversation, row.MessageId),
        };
    }

    private static List<string> ReadReplyTargets(
        SqliteConnection connection,
        ConversationDescriptor conversation,
        string messageId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT target_message_id
            FROM reply_targets
            WHERE account_id = $accountId
              AND conversation_type = $conversationType
              AND conversation_id = $conversationId
              AND message_id = $messageId
            ORDER BY target_message_id;
            """;
        command.Parameters.AddWithValue("$accountId", conversation.AccountId);
        command.Parameters.AddWithValue("$conversationType", (int)conversation.Type);
        command.Parameters.AddWithValue("$conversationId", conversation.Id);
        command.Parameters.AddWithValue("$messageId", messageId);
        using var reader = command.ExecuteReader();
        var targets = new List<string>();
        while (reader.Read())
        {
            targets.Add(reader.GetString(0));
        }

        return targets;
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

    private sealed class IndexedRow
    {
        public required string MessageId { get; init; }

        public required long Timestamp { get; init; }

        public required int Direction { get; init; }

        public required string SenderId { get; init; }

        public string? SenderDisplayName { get; init; }

        public string? BodyJson { get; init; }

        public required int ConversationType { get; init; }

        public required string ConversationId { get; init; }

        public required string ConversationDisplayName { get; init; }
    }
}
