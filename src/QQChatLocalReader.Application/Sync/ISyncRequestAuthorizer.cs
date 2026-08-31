using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Application.Sync;

public interface ISyncRequestAuthorizer
{
    Task<bool> AuthorizeAsync(SyncRequest request, CancellationToken cancellationToken);
}
