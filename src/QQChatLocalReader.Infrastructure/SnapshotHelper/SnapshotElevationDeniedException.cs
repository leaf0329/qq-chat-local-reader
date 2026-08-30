namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

public sealed class SnapshotElevationDeniedException : Exception
{
    public SnapshotElevationDeniedException()
        : base("Administrator approval was cancelled, so the QQ database was not synchronized.")
    {
    }
}
