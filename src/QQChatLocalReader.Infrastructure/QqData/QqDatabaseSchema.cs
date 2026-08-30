namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqDatabaseSchema
{
    public QqDatabaseSchema(IReadOnlyList<QqTableSchema> tables)
    {
        Tables = tables;
    }

    public IReadOnlyList<QqTableSchema> Tables { get; }

    public override string ToString() => $"{nameof(QqDatabaseSchema)} {{ Tables = {Tables.Count} }}";
}
