using QQChatLocalReader.Application.Sync;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.Indexing;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.QqData.MessageBodies;

namespace QQChatLocalReader.Infrastructure.Tests.Application;

public sealed class SyncJobManagerTests : IDisposable
{
    private readonly string testRoot = Path.Combine(
        Path.GetTempPath(),
        $"qq-reader-jobs-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task AuthorizedJobCommitsAndReportsCount()
    {
        using var index = EncryptedMessageIndex.Open(testRoot);
        using var manager = new SyncJobManager(
            new StubSource([CreateMessage()]),
            new StubAuthorizer(allowed: true),
            index);

        var jobId = manager.Start(CreateRequest());
        var result = await WaitForTerminalAsync(manager, jobId);

        Assert.Equal(SyncJobState.Completed, result.State);
        Assert.Equal(1, result.MessageCount);
        Assert.Null(result.ErrorCode);
        Assert.Single(index.ReadMessages(
            CreateRequest().Conversations[0],
            CreateRequest().Range));
    }

    [Fact]
    public async Task RejectedAndCanceledJobsDoNotWrite()
    {
        using var index = EncryptedMessageIndex.Open(testRoot);
        using var rejectedManager = new SyncJobManager(
            new StubSource([CreateMessage()]),
            new StubAuthorizer(allowed: false),
            index);
        var rejected = await WaitForTerminalAsync(rejectedManager, rejectedManager.Start(CreateRequest()));
        Assert.Equal(SyncJobState.Rejected, rejected.State);

        using var canceledManager = new SyncJobManager(
            new BlockingSource(),
            new StubAuthorizer(allowed: true),
            index);
        var canceledId = canceledManager.Start(CreateRequest());
        Assert.True(canceledManager.Cancel(canceledId));
        var canceled = await WaitForTerminalAsync(canceledManager, canceledId);
        Assert.Equal(SyncJobState.Canceled, canceled.State);
        Assert.Empty(index.ReadMessages(CreateRequest().Conversations[0], CreateRequest().Range));
    }

    public void Dispose()
    {
        if (Directory.Exists(testRoot))
        {
            Directory.Delete(testRoot, recursive: true);
        }
    }

    private static async Task<SyncJobSnapshot> WaitForTerminalAsync(SyncJobManager manager, Guid jobId)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true)
        {
            var snapshot = manager.Get(jobId);
            if (snapshot.State is SyncJobState.Completed or SyncJobState.Rejected or
                SyncJobState.Canceled or SyncJobState.Failed)
            {
                return snapshot;
            }

            await Task.Delay(10, timeout.Token);
        }
    }

    private static SyncRequest CreateRequest()
    {
        var conversation = new ConversationDescriptor("10001", ConversationType.Private, "20002", "Masked peer");
        return new SyncRequest(
            "10001",
            [conversation],
            new TimeRange(
                DateTimeOffset.FromUnixTimeSeconds(1_699_999_999),
                DateTimeOffset.FromUnixTimeSeconds(1_700_000_010)));
    }

    private static QqMessageRecord CreateMessage() => new()
    {
        AccountId = "10001",
        ConversationType = ConversationType.Private,
        ConversationId = "20002",
        ConversationDisplayName = "Masked peer",
        StableMessageId = "1",
        TimestampUtc = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
        RawDirection = 0,
        SenderId = "20002",
        Body = new QqMessageBody(
            QqMessageBodyParseStatus.Complete,
            [new QqMessageSegment { RawContentType = 1, Text = "test" }],
            0),
    };

    private sealed class StubAuthorizer(bool allowed) : ISyncRequestAuthorizer
    {
        public Task<bool> AuthorizeAsync(SyncRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(allowed);
    }

    private sealed class StubSource(IReadOnlyList<QqMessageRecord> messages) : IMessageSyncSource
    {
        public Task<IReadOnlyList<QqMessageRecord>> ReadMessagesAsync(
            SyncRequest request,
            CancellationToken cancellationToken) => Task.FromResult(messages);
    }

    private sealed class BlockingSource : IMessageSyncSource
    {
        public async Task<IReadOnlyList<QqMessageRecord>> ReadMessagesAsync(
            SyncRequest request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }
}
