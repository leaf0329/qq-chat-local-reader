namespace QQChatLocalReader.Core.Models;

public sealed record GroupMemberDescriptor(string GroupId, string MemberId, string DisplayName)
{
    public override string ToString() => $"{nameof(GroupMemberDescriptor)} {{ sensitive values omitted }}";
}
