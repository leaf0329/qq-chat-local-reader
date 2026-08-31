using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Indexing;

public sealed record MessageSearchPage(
    IReadOnlyList<QqMessageRecord> Messages,
    string? NextCursor);
