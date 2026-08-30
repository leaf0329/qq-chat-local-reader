namespace QQChatLocalReader.Core.Models;

public sealed record ConversationDescriptor
{
    public ConversationDescriptor(
        string accountId,
        ConversationType type,
        string id,
        string displayName)
    {
        AccountId = RequireValue(accountId, nameof(accountId));
        Type = type;
        Id = RequireValue(id, nameof(id));
        DisplayName = RequireValue(displayName, nameof(displayName));
    }

    public string AccountId { get; }

    public ConversationType Type { get; }

    public string Id { get; }

    public string DisplayName { get; }

    public string StableKey => $"{AccountId}:{(int)Type}:{Id}";

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : value;
}
