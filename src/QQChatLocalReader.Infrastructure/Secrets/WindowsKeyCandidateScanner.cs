namespace QQChatLocalReader.Infrastructure.Secrets;

public sealed class WindowsKeyCandidateScanner : IKeyCandidateScanner
{
    public ProcessMemoryScanResult Scan(
        int processId,
        KeyCandidateVisitor visitor,
        CancellationToken cancellationToken = default) =>
        ReadOnlyProcessMemoryScanner.Scan(processId, visitor, cancellationToken);
}
