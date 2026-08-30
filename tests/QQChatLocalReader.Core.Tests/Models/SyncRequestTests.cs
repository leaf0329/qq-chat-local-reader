using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Core.Tests.Models;

public sealed class SyncRequestTests
{
    private static readonly TimeRange Range = new(
        new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 8, 31, 0, 0, 0, TimeSpan.Zero));

    [Fact]
    public void RequiresAtLeastOneConversation()
    {
        Assert.Throws<ArgumentException>(() =>
            new SyncRequest("account", [], Range));
    }

    [Fact]
    public void RejectsConversationFromAnotherAccount()
    {
        var conversation = new ConversationDescriptor(
            "other-account",
            ConversationType.Group,
            "group",
            "Group");

        Assert.Throws<ArgumentException>(() =>
            new SyncRequest("account", [conversation], Range));
    }

    [Fact]
    public void RemovesDuplicateStableConversationKeys()
    {
        var conversation = new ConversationDescriptor(
            "account",
            ConversationType.Group,
            "group",
            "Group");

        var request = new SyncRequest(
            "account",
            [conversation, conversation],
            Range);

        Assert.Single(request.Conversations);
    }
}
