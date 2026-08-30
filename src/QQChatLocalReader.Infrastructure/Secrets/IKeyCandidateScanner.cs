namespace QQChatLocalReader.Infrastructure.Secrets;

public interface IKeyCandidateScanner
{
    ProcessMemoryScanResult Scan(
        int processId,
        KeyCandidateVisitor visitor,
        CancellationToken cancellationToken = default);
}
