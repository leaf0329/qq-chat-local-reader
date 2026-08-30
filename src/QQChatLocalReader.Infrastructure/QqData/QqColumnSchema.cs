namespace QQChatLocalReader.Infrastructure.QqData;

public sealed record QqColumnSchema(
    string Name,
    string DeclaredType,
    bool IsRequired,
    int PrimaryKeyOrder,
    bool IsHidden);
