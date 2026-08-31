namespace QQChatLocalReader.Infrastructure.Exporting;

public sealed record MessageExportResult(
    string FilePath,
    MessageExportFormat Format,
    int MessageCount);
