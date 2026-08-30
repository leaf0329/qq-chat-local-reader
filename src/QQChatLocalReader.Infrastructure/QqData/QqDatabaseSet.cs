namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqDatabaseSet
{
    public QqDatabaseSet(string accountId, string databasePath, IReadOnlyList<string> companionPaths)
    {
        AccountId = accountId;
        DatabasePath = databasePath;
        CompanionPaths = companionPaths;
    }

    public string AccountId { get; }

    public string DatabasePath { get; }

    public IReadOnlyList<string> CompanionPaths { get; }

    public override string ToString() => $"{nameof(QqDatabaseSet)} {{ sensitive values omitted }}";
}
