using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Indexing;

public sealed record MessageContext(
    IReadOnlyList<QqMessageRecord> Messages,
    int AnchorIndex);
