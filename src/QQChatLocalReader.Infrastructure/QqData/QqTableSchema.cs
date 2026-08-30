namespace QQChatLocalReader.Infrastructure.QqData;

public sealed class QqTableSchema
{
    public QqTableSchema(string name, IReadOnlyList<QqColumnSchema> columns)
    {
        Name = name;
        Columns = columns;
    }

    public string Name { get; }

    public IReadOnlyList<QqColumnSchema> Columns { get; }

    public override string ToString() => $"{nameof(QqTableSchema)} {{ Columns = {Columns.Count} }}";
}
