namespace QQChatLocalReader.Application.Sync;

public enum SyncJobState
{
    AwaitingAuthorization = 1,
    Running = 2,
    Completed = 3,
    Rejected = 4,
    Canceled = 5,
    Failed = 6,
}
