using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Infrastructure.Secrets;

public sealed class QqDatabaseKeyResolver
{
    private readonly IKeyCandidateScanner scanner;

    public QqDatabaseKeyResolver(IKeyCandidateScanner scanner)
    {
        this.scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
    }

    public QqDatabaseKey Resolve(
        int processId,
        QqPreparedDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        byte[]? matchedBytes = null;
        scanner.Scan(
            processId,
            candidate =>
            {
                if (!QqSqlCipherCandidateValidator.IsCandidateValid(database, candidate))
                {
                    return false;
                }

                matchedBytes = candidate.ToArray();
                return true;
            },
            cancellationToken);

        if (matchedBytes is null)
        {
            throw new InvalidDataException("No database key candidate passed SQLCipher and schema validation.");
        }

        var key = new QqDatabaseKey(matchedBytes);
        try
        {
            key.Use(candidate =>
            {
                QqSqlCipherCandidateValidator.ValidateIntegrity(database, candidate);
                return true;
            });
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }
}
