using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqRuntime;
using QQChatLocalReader.Infrastructure.Secrets;
using QQChatLocalReader.Infrastructure.SnapshotHelper;

namespace QQChatLocalReader.Application.Sync;

public sealed class LocalQqMessageSyncSource : IMessageSyncSource
{
    private readonly ElevatedSnapshotClient snapshotClient;

    public LocalQqMessageSyncSource(string snapshotHelperExecutablePath)
    {
        snapshotClient = new ElevatedSnapshotClient(snapshotHelperExecutablePath);
    }

    public async Task<IReadOnlyList<QqMessageRecord>> ReadMessagesAsync(
        SyncRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var dataRoot = await QqUserDataConfiguration
            .ReadDataRootAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var databaseSet = QqDatabaseDiscovery.Discover(dataRoot)
            .SingleOrDefault(item => item.AccountId.Equals(request.AccountId, StringComparison.Ordinal)) ??
            throw new InvalidOperationException("The selected QQ account database is unavailable.");
        var runtime = QqProcessDiscovery.Discover()
            .FirstOrDefault(item => item.Version.Equals(QqNtMessageDatabaseAdapter.SupportedVersion, StringComparison.Ordinal)) ??
            throw new InvalidOperationException("A supported running QQ process is unavailable.");

        await using var snapshot = await snapshotClient
            .CreateAsync(databaseSet, cancellationToken)
            .ConfigureAwait(false);
        await using var prepared = await QqDatabaseImagePreparer
            .PrepareAsync(snapshot, cancellationToken)
            .ConfigureAwait(false);
        var resolver = new QqDatabaseKeyResolver(new WindowsKeyCandidateScanner());
        using var key = resolver.Resolve(runtime.ProcessId, prepared, cancellationToken);
        var adapter = QqNtMessageDatabaseAdapter.Open(
            runtime.Version,
            databaseSet.AccountId,
            prepared,
            key);
        var availableKeys = adapter.ListConversations()
            .Select(item => item.StableKey)
            .ToHashSet(StringComparer.Ordinal);
        if (request.Conversations.Any(item => !availableKeys.Contains(item.StableKey)))
        {
            throw new InvalidOperationException("A requested conversation is not present in the selected QQ account.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        return adapter.ReadMessages(request);
    }
}
