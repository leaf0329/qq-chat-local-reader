namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

internal sealed class SnapshotHelperRequest
{
    public required string DatabasePath { get; init; }

    public required string[] CompanionPaths { get; init; }

    public required string SnapshotRoot { get; init; }

    public override string ToString() => $"{nameof(SnapshotHelperRequest)} {{ sensitive values omitted }}";
}
