namespace QQChatLocalReader.Core.Models;

public sealed record SyncRequest
{
    public SyncRequest(
        string accountId,
        IEnumerable<ConversationDescriptor> conversations,
        TimeRange range,
        bool includeForwarded = false)
    {
        AccountId = string.IsNullOrWhiteSpace(accountId)
            ? throw new ArgumentException("Account ID cannot be empty.", nameof(accountId))
            : accountId;
        ArgumentNullException.ThrowIfNull(conversations);
        Range = range ?? throw new ArgumentNullException(nameof(range));

        Conversations = conversations
            .DistinctBy(conversation => conversation.StableKey)
            .ToArray();

        if (Conversations.Count == 0)
        {
            throw new ArgumentException(
                "At least one conversation must be selected.",
                nameof(conversations));
        }

        if (Conversations.Any(conversation => conversation.AccountId != AccountId))
        {
            throw new ArgumentException(
                "Every conversation must belong to the selected account.",
                nameof(conversations));
        }

        IncludeForwarded = includeForwarded;
    }

    public string AccountId { get; }

    public IReadOnlyList<ConversationDescriptor> Conversations { get; }

    public TimeRange Range { get; }

    public bool IncludeForwarded { get; }
}
