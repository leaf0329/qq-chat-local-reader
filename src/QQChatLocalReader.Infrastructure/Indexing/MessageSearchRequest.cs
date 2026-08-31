using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Infrastructure.Indexing;

public sealed class MessageSearchRequest
{
    public const int DefaultPageSize = 100;
    public const int MaximumPageSize = 500;

    public MessageSearchRequest(
        string accountId,
        IEnumerable<ConversationDescriptor> conversations,
        TimeRange range,
        string? keyword = null,
        string? senderId = null,
        int pageSize = DefaultPageSize,
        string? cursor = null)
    {
        AccountId = string.IsNullOrWhiteSpace(accountId)
            ? throw new ArgumentException("Account ID cannot be empty.", nameof(accountId))
            : accountId;
        ArgumentNullException.ThrowIfNull(conversations);
        Range = range ?? throw new ArgumentNullException(nameof(range));
        Conversations = conversations.DistinctBy(item => item.StableKey).ToArray();
        if (Conversations.Count == 0 || Conversations.Any(item => item.AccountId != AccountId))
        {
            throw new ArgumentException("At least one conversation from the selected account is required.", nameof(conversations));
        }

        if (pageSize is < 1 or > MaximumPageSize)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize));
        }

        Keyword = Normalize(keyword);
        SenderId = Normalize(senderId);
        PageSize = pageSize;
        Cursor = Normalize(cursor);
    }

    public string AccountId { get; }

    public IReadOnlyList<ConversationDescriptor> Conversations { get; }

    public TimeRange Range { get; }

    public string? Keyword { get; }

    public string? SenderId { get; }

    public int PageSize { get; }

    public string? Cursor { get; }

    public override string ToString() =>
        $"{nameof(MessageSearchRequest)} {{ Conversations = {Conversations.Count}, PageSize = {PageSize}, sensitive filters omitted }}";

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
