namespace QQChatLocalReader.Core.Models;

public sealed record AccountDescriptor
{
    public AccountDescriptor(string id, string displayName)
    {
        Id = RequireValue(id, nameof(id));
        DisplayName = RequireValue(displayName, nameof(displayName));
    }

    public string Id { get; }

    public string DisplayName { get; }

    private static string RequireValue(string value, string parameterName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value cannot be empty.", parameterName)
            : value;
}
