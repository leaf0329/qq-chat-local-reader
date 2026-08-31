using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Infrastructure.Indexing;

public sealed record MessageIndexStatus(int MessageCount, IReadOnlyList<ConversationIndexCoverage> Conversations);

public sealed record ConversationIndexCoverage(
    string AccountId,
    ConversationType Type,
    string ConversationId,
    string DisplayName,
    int MessageCount,
    DateTimeOffset? FirstMessageUtc,
    DateTimeOffset? LastMessageUtc);
