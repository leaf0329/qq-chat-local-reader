using System.Text.Json;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.Exporting;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.Tests.Exporting;

public sealed class MessageExporterTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-export-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task BasicRedactionIsStableWithinOneExportAndDifferentAcrossExports()
    {
        var messages = new[]
        {
            CreateMessage("1", "号码12345678 手机13812345678 身份11010519491231002X C:\\Users\\Secret\\a.txt"),
            CreateMessage("2", "再次出现12345678"),
        };

        var first = await MessageExporter.ExportAsync(
            messages, testRoot, MessageExportFormat.Json, MessageExportPrivacy.BasicRedaction);
        var second = await MessageExporter.ExportAsync(
            messages, testRoot, MessageExportFormat.Json, MessageExportPrivacy.BasicRedaction);
        var firstText = await File.ReadAllTextAsync(first.FilePath);
        var secondText = await File.ReadAllTextAsync(second.FilePath);

        Assert.Equal(2, first.MessageCount);
        Assert.DoesNotContain("12345678", firstText, StringComparison.Ordinal);
        Assert.DoesNotContain("13812345678", firstText, StringComparison.Ordinal);
        Assert.DoesNotContain("11010519491231002X", firstText, StringComparison.Ordinal);
        Assert.DoesNotContain("C:\\Users", firstText, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(firstText, secondText);
        using var document = JsonDocument.Parse(firstText);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(
            rows[0].GetProperty("SenderId").GetString(),
            rows[1].GetProperty("SenderId").GetString());
        Assert.DoesNotContain(".tmp", Directory.EnumerateFiles(testRoot), StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CsvPreventsFormulaExecutionAndMarkdownEscapesHtml()
    {
        var message = CreateMessage("1", "  =SUM(A1:A2)<script>alert(1)</script>");

        var csv = await MessageExporter.ExportAsync([message], testRoot, MessageExportFormat.Csv);
        var markdown = await MessageExporter.ExportAsync([message], testRoot, MessageExportFormat.Markdown);
        var csvText = await File.ReadAllTextAsync(csv.FilePath);
        var markdownText = await File.ReadAllTextAsync(markdown.FilePath);

        Assert.Contains("\"'  =SUM(A1:A2)<script>", csvText, StringComparison.Ordinal);
        Assert.DoesNotContain("<script>", markdownText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;script&gt;", markdownText, StringComparison.Ordinal);
        Assert.Equal(1, csv.MessageCount);
        Assert.Equal(1, markdown.MessageCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static QqMessageRecord CreateMessage(string messageId, string text) => new()
    {
        AccountId = "10001",
        ConversationType = ConversationType.Private,
        ConversationId = "20002",
        ConversationDisplayName = "联系人12345678",
        StableMessageId = messageId,
        TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
        RawDirection = 0,
        SenderId = "30003",
        SenderDisplayName = "发送者13812345678",
        Body = new QqMessageBody(
            QqMessageBodyParseStatus.Complete,
            [new QqMessageSegment { RawContentType = (int)QqMessageContentType.Text, Text = text }],
            0),
        ReplyTargetMessageIds = ["9"],
    };
}
