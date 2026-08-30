namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

internal sealed class SnapshotHelperResponse
{
    public required bool Success { get; init; }

    public string? ErrorCode { get; init; }

    public string? DirectoryPath { get; init; }

    public string? DatabasePath { get; init; }

    public string[] CompanionPaths { get; init; } = [];

    public override string ToString() => $"{nameof(SnapshotHelperResponse)} {{ Success = {Success}, ErrorCode = {ErrorCode} }}";
}
