using System.Text;
using System.Text.Json;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.Exporting;

public static class MessageExporter
{
    public static async Task<MessageExportResult> ExportAsync(
        IEnumerable<QqMessageRecord> messages,
        string outputDirectory,
        MessageExportFormat format,
        MessageExportPrivacy privacy = MessageExportPrivacy.Raw,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        if (!Enum.IsDefined(format))
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        if (!Enum.IsDefined(privacy))
        {
            throw new ArgumentOutOfRangeException(nameof(privacy));
        }

        var source = messages.ToArray();
        var fullDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(fullDirectory);
        var extension = format switch
        {
            MessageExportFormat.Markdown => "md",
            MessageExportFormat.Json => "json",
            MessageExportFormat.Csv => "csv",
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
        var fileName = $"qq-chat-export-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.{extension}";
        var finalPath = Path.Combine(fullDirectory, fileName);
        var temporaryPath = finalPath + ".tmp";

        using var redactor = privacy == MessageExportPrivacy.BasicRedaction
            ? new BasicMessageRedactor()
            : null;
        var rows = source.Select(message => CreateRow(message, redactor)).ToArray();
        try
        {
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                switch (format)
                {
                    case MessageExportFormat.Markdown:
                        await WriteMarkdownAsync(stream, rows, cancellationToken).ConfigureAwait(false);
                        break;
                    case MessageExportFormat.Json:
                        await JsonSerializer.SerializeAsync(stream, rows, cancellationToken: cancellationToken)
                            .ConfigureAwait(false);
                        break;
                    case MessageExportFormat.Csv:
                        await WriteCsvAsync(stream, rows, cancellationToken).ConfigureAwait(false);
                        break;
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, finalPath);
            return new MessageExportResult(finalPath, format, rows.Length);
        }
        catch
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }

            throw;
        }
    }

    private static ExportRow CreateRow(QqMessageRecord message, BasicMessageRedactor? redactor)
    {
        var text = string.Join("\n", message.Body?.Segments.Select(FormatSegment) ?? []);
        return new ExportRow
        {
            AccountId = redactor?.Identity("账号", message.AccountId) ?? message.AccountId,
            ConversationType = message.ConversationType.ToString(),
            ConversationId = redactor?.Identity("会话", message.ConversationId) ?? message.ConversationId,
            ConversationName = redactor?.VisibleText(message.ConversationDisplayName) ?? message.ConversationDisplayName,
            MessageId = redactor?.Identity("消息", message.StableMessageId) ?? message.StableMessageId,
            TimestampUtc = message.TimestampUtc,
            SenderId = redactor?.Identity("用户", message.SenderId) ?? message.SenderId,
            SenderName = redactor?.VisibleText(message.SenderDisplayName) ?? message.SenderDisplayName,
            Text = redactor?.VisibleText(text) ?? text,
            Attachments = message.Body?.Segments
                .Where(segment => segment.Media is not null)
                .Select(segment => CreateAttachment(segment, redactor))
                .ToArray() ?? [],
            ReplyTargetMessageIds = message.ReplyTargetMessageIds
                .Select(target => redactor?.Identity("消息", target) ?? target)
                .ToArray(),
        };
    }

    private static ExportAttachment CreateAttachment(
        QqMessageSegment segment,
        BasicMessageRedactor? redactor)
    {
        var media = segment.Media!;
        return new ExportAttachment
        {
            Type = segment.ContentType.ToString(),
            FileName = redactor?.VisibleText(media.FileName) ?? media.FileName,
            LocalPath = redactor is null ? media.LocalPath : BasicMessageRedactor.LocalPath(media.LocalPath),
            FileSize = media.FileSize,
            DurationSeconds = media.DurationSeconds,
            Width = media.Width,
            Height = media.Height,
            FileExtension = redactor?.VisibleText(media.FileExtension) ?? media.FileExtension,
            PreviewPath = redactor is null ? media.PreviewPath : BasicMessageRedactor.LocalPath(media.PreviewPath),
        };
    }

    private static string FormatSegment(QqMessageSegment segment)
    {
        if (segment.Text is not null)
        {
            return segment.Text;
        }

        if (segment.ContentType == QqMessageContentType.QqFace)
        {
            return segment.EmojiText ?? "[QQ表情]";
        }

        if (segment.ContentType == QqMessageContentType.Reply)
        {
            return segment.Reply?.Summary is null ? "[引用]" : $"[引用: {segment.Reply.Summary}]";
        }

        if (segment.Media is not null)
        {
            var name = segment.Media.FileName is null ? string.Empty : $": {segment.Media.FileName}";
            return $"[{ContentTypeName(segment.ContentType)}{name}]";
        }

        return $"[{ContentTypeName(segment.ContentType)}]";
    }

    private static string ContentTypeName(QqMessageContentType type) => type switch
    {
        QqMessageContentType.Image => "图片",
        QqMessageContentType.File => "文件",
        QqMessageContentType.Voice => "语音",
        QqMessageContentType.Video => "视频",
        QqMessageContentType.GrayTip => "系统提示",
        QqMessageContentType.MarketFace => "商城表情",
        QqMessageContentType.Markdown => "Markdown消息",
        QqMessageContentType.LegacyForward => "合并转发",
        QqMessageContentType.Unknown => "未知消息",
        _ => type.ToString(),
    };

    private static async Task WriteMarkdownAsync(
        Stream stream,
        IReadOnlyList<ExportRow> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("# QQ 聊天记录导出").ConfigureAwait(false);
        await writer.WriteLineAsync().ConfigureAwait(false);
        await writer.WriteLineAsync("| 时间（UTC） | 会话 | 发送者 | 消息 ID | 内容 | 附件元数据 |").ConfigureAwait(false);
        await writer.WriteLineAsync("| --- | --- | --- | --- | --- | --- |").ConfigureAwait(false);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(
                $"| {Markdown(row.TimestampUtc.ToString("O"))} | {Markdown(row.ConversationName)} | {Markdown(row.SenderName ?? row.SenderId)} | {Markdown(row.MessageId)} | {Markdown(row.Text)} | {Markdown(JsonSerializer.Serialize(row.Attachments))} |")
                .ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteCsvAsync(
        Stream stream,
        IReadOnlyList<ExportRow> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), leaveOpen: true);
        await writer.WriteLineAsync("account_id,conversation_type,conversation_id,conversation_name,message_id,timestamp_utc,sender_id,sender_name,text,reply_target_ids,attachments_json")
            .ConfigureAwait(false);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var values = new[]
            {
                row.AccountId,
                row.ConversationType,
                row.ConversationId,
                row.ConversationName,
                row.MessageId,
                row.TimestampUtc.ToString("O"),
                row.SenderId,
                row.SenderName ?? string.Empty,
                row.Text,
                string.Join(';', row.ReplyTargetMessageIds),
                JsonSerializer.Serialize(row.Attachments),
            };
            await writer.WriteLineAsync(string.Join(',', values.Select(Csv))).ConfigureAwait(false);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string Markdown(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("|", "\\|", StringComparison.Ordinal)
        .Replace("\r\n", "<br>", StringComparison.Ordinal)
        .Replace("\n", "<br>", StringComparison.Ordinal)
        .Replace("\r", "<br>", StringComparison.Ordinal);

    private static string Csv(string value)
    {
        var candidate = value.AsSpan().TrimStart();
        var safe = candidate.Length > 0 && candidate[0] is '=' or '+' or '-' or '@'
            ? $"'{value}"
            : value;
        return $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private sealed class ExportRow
    {
        public required string AccountId { get; init; }
        public required string ConversationType { get; init; }
        public required string ConversationId { get; init; }
        public required string ConversationName { get; init; }
        public required string MessageId { get; init; }
        public required DateTimeOffset TimestampUtc { get; init; }
        public required string SenderId { get; init; }
        public string? SenderName { get; init; }
        public required string Text { get; init; }
        public required IReadOnlyList<string> ReplyTargetMessageIds { get; init; }
        public required IReadOnlyList<ExportAttachment> Attachments { get; init; }
    }

    private sealed class ExportAttachment
    {
        public required string Type { get; init; }
        public string? FileName { get; init; }
        public string? LocalPath { get; init; }
        public long? FileSize { get; init; }
        public int? DurationSeconds { get; init; }
        public int? Width { get; init; }
        public int? Height { get; init; }
        public string? FileExtension { get; init; }
        public string? PreviewPath { get; init; }
    }
}
