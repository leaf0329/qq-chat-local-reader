using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Application.Sync;

public interface ILocalQqCatalog
{
    Task<IReadOnlyList<AccountDescriptor>> ListAccountsAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ConversationDescriptor>> ListConversationsAsync(
        string accountId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<GroupMemberDescriptor>> ListGroupMembersAsync(
        string accountId,
        string groupId,
        CancellationToken cancellationToken);
}
