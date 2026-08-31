using System.Text;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.Indexing;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.Tests.Indexing;

public sealed class EncryptedMessageIndexTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-index-test-{Guid.NewGuid():N}");

    [Fact]
    public void IndexIsEncryptedReopenableAndIdempotent()
    {
        var conversation = new ConversationDescriptor("10001", ConversationType.Group, "30003", "Masked group");
        var range = new TimeRange(
            DateTimeOffset.FromUnixTimeSeconds(1_699_999_999),
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_010));
        var firstObservation = CreateMessage("first text", "Masked sender", ["8"]);
        var laterObservation = CreateMessage("updated text", "Updated sender", ["9"]);

        using (var index = EncryptedMessageIndex.Open(testRoot))
        {
            Assert.Equal(1, index.UpsertMessages([firstObservation]));
            Assert.Equal(1, index.UpsertMessages([laterObservation]));
        }

        var directorySecurity = new DirectoryInfo(testRoot).GetAccessControl();
        Assert.True(directorySecurity.AreAccessRulesProtected);
        using var identity = WindowsIdentity.GetCurrent();
        Assert.Contains(
            directorySecurity.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<FileSystemAccessRule>(),
            rule => rule.IdentityReference == identity.User &&
                rule.AccessControlType == AccessControlType.Allow &&
                rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));

        var databaseBytes = File.ReadAllBytes(Path.Combine(testRoot, "messages.db"));
        Assert.False(databaseBytes.AsSpan().StartsWith("SQLite format 3"u8));
        Assert.DoesNotContain("updated text", Encoding.UTF8.GetString(databaseBytes), StringComparison.Ordinal);
        Assert.DoesNotContain("updated text", File.ReadAllText(Path.Combine(testRoot, "index.key")), StringComparison.Ordinal);

        using var reopened = EncryptedMessageIndex.Open(testRoot);
        var result = Assert.Single(reopened.ReadMessages(conversation, range));
        Assert.Equal("42", result.StableMessageId);
        Assert.Equal("Updated sender", result.SenderDisplayName);
        Assert.Equal("updated text", Assert.Single(result.Body!.Segments).Text);
        Assert.Equal(["9"], result.ReplyTargetMessageIds);
        Assert.DoesNotContain(testRoot, reopened.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FailedBatchRollsBackEarlierMessages()
    {
        using var index = EncryptedMessageIndex.Open(testRoot);
        var valid = CreateMessage("first", "sender", [], "41");
        var invalid = CreateMessage(
            "second",
            "sender",
            [],
            "42",
            new QqMessageBody(
                QqMessageBodyParseStatus.Partial,
                [CreateDeepSegment(70)],
                0));

        Assert.Throws<JsonException>(() => index.UpsertMessages([valid, invalid]));

        var conversation = new ConversationDescriptor("10001", ConversationType.Group, "30003", "Masked group");
        var range = new TimeRange(
            DateTimeOffset.FromUnixTimeSeconds(1_699_999_999),
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_010));
        Assert.Empty(index.ReadMessages(conversation, range));
    }

    [Fact]
    public void ExistingIndexWithoutItsKeyIsNotReinitialized()
    {
        using (EncryptedMessageIndex.Open(testRoot))
        {
        }

        var databasePath = Path.Combine(testRoot, "messages.db");
        var originalLength = new FileInfo(databasePath).Length;
        File.Delete(Path.Combine(testRoot, "index.key"));

        Assert.Throws<InvalidDataException>(() => EncryptedMessageIndex.Open(testRoot));
        Assert.Equal(originalLength, new FileInfo(databasePath).Length);
    }

    [Fact]
    public void SearchUsesExplicitScopeFiltersAndStableCursor()
    {
        var group = new ConversationDescriptor("10001", ConversationType.Group, "30003", "Masked group");
        var peer = new ConversationDescriptor("10001", ConversationType.Private, "20002", "Masked peer");
        using var index = EncryptedMessageIndex.Open(testRoot);
        index.UpsertMessages([
            CreateSearchMessage(group, "1", 1_700_000_000, "alpha", "70001"),
            CreateSearchMessage(group, "2", 1_700_000_001, "中文关键词一", "70002"),
            CreateSearchMessage(peer, "3", 1_700_000_002, "中文关键词二", "70002"),
            CreateSearchMessage(group, "4", 1_700_000_003, "中文关键词三", "70002"),
        ]);
        var range = new TimeRange(
            DateTimeOffset.FromUnixTimeSeconds(1_699_999_999),
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_010));

        var firstPage = index.SearchMessages(new MessageSearchRequest(
            "10001", [group, peer], range, keyword: "中文关键词", pageSize: 2));

        Assert.Equal(["2", "3"], firstPage.Messages.Select(message => message.StableMessageId));
        Assert.NotNull(firstPage.NextCursor);
        var secondPage = index.SearchMessages(new MessageSearchRequest(
            "10001", [group, peer], range, keyword: "中文关键词", pageSize: 2, cursor: firstPage.NextCursor));
        Assert.Equal(["4"], secondPage.Messages.Select(message => message.StableMessageId));
        Assert.Null(secondPage.NextCursor);

        var senderResult = index.SearchMessages(new MessageSearchRequest(
            "10001", [group], range, senderId: "70001"));
        Assert.Equal(["1"], senderResult.Messages.Select(message => message.StableMessageId));
        Assert.Throws<ArgumentException>(() => index.SearchMessages(new MessageSearchRequest(
            "10001", [group], range, cursor: "not-a-cursor")));

        var context = index.ReadContext(group, "2", before: 1, after: 2);
        Assert.Equal(1, context.AnchorIndex);
        Assert.Equal(["1", "2", "4"], context.Messages.Select(message => message.StableMessageId));
        Assert.Throws<ArgumentOutOfRangeException>(() => index.ReadContext(group, "2", before: 101));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static QqMessageRecord CreateMessage(
        string text,
        string senderDisplayName,
        IReadOnlyList<string> replyTargets,
        string stableMessageId = "42",
        QqMessageBody? body = null) => new()
    {
        AccountId = "10001",
        ConversationType = ConversationType.Group,
        ConversationId = "30003",
        ConversationDisplayName = "Masked group",
        StableMessageId = stableMessageId,
        TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_001),
        RawDirection = 1,
        SenderId = "20002",
        SenderDisplayName = senderDisplayName,
        Body = body ?? new QqMessageBody(
            QqMessageBodyParseStatus.Complete,
            [new QqMessageSegment { RawContentType = 1, Text = text }],
            0),
        ReplyTargetMessageIds = replyTargets,
    };

    private static QqMessageSegment CreateDeepSegment(int depth) => new()
    {
        RawContentType = (int)QqMessageContentType.Reply,
        Reply = depth == 0
            ? new QqReplyReference { Summary = "end" }
            : new QqReplyReference { EmbeddedContent = CreateDeepSegment(depth - 1) },
    };

    private static QqMessageRecord CreateSearchMessage(
        ConversationDescriptor conversation,
        string messageId,
        long timestamp,
        string text,
        string senderId) => new()
    {
        AccountId = conversation.AccountId,
        ConversationType = conversation.Type,
        ConversationId = conversation.Id,
        ConversationDisplayName = conversation.DisplayName,
        StableMessageId = messageId,
        TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(timestamp),
        RawDirection = 0,
        SenderId = senderId,
        SenderDisplayName = "Masked sender",
        Body = new QqMessageBody(
            QqMessageBodyParseStatus.Complete,
            [new QqMessageSegment { RawContentType = (int)QqMessageContentType.Text, Text = text }],
            0),
    };
}
