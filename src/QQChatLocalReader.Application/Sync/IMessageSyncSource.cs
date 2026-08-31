using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.QqData;

namespace QQChatLocalReader.Application.Sync;

public interface IMessageSyncSource
{
    Task<IReadOnlyList<QqMessageRecord>> ReadMessagesAsync(
        SyncRequest request,
        CancellationToken cancellationToken);
}
