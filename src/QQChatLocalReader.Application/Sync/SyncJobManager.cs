using System.Collections.Concurrent;
using System.Text.Json;
using QQChatLocalReader.Core.Models;
using QQChatLocalReader.Infrastructure.Indexing;

namespace QQChatLocalReader.Application.Sync;

public sealed class SyncJobManager : IDisposable
{
    private readonly IMessageSyncSource source;
    private readonly ISyncRequestAuthorizer authorizer;
    private readonly EncryptedMessageIndex index;
    private readonly ConcurrentDictionary<Guid, Job> jobs = new();
    private readonly SemaphoreSlim syncGate = new(1, 1);
    private bool disposed;

    public SyncJobManager(
        IMessageSyncSource source,
        ISyncRequestAuthorizer authorizer,
        EncryptedMessageIndex index)
    {
        this.source = source ?? throw new ArgumentNullException(nameof(source));
        this.authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        this.index = index ?? throw new ArgumentNullException(nameof(index));
        foreach (var persisted in index.ReadSyncJobs())
        {
            var job = new Job(persisted);
            if (job.Snapshot().State is SyncJobState.AwaitingAuthorization or SyncJobState.Running)
            {
                job.Update(SyncJobState.Failed, errorCode: "interrupted_by_restart");
                Persist(job);
            }

            jobs.TryAdd(job.Id, job);
        }
    }

    public Guid Start(SyncRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var job = new Job(Guid.NewGuid(), SerializeRequest(request));
        if (!jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException("A unique synchronization job could not be created.");
        }

        Persist(job);
        job.Execution = RunAsync(job, request);
        return job.Id;
    }

    public Guid Restart(Guid jobId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!jobs.TryGetValue(jobId, out var job))
        {
            throw new KeyNotFoundException("The synchronization job was not found.");
        }

        return Start(DeserializeRequest(job.RequestJson));
    }

    public SyncJobSnapshot Get(Guid jobId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return jobs.TryGetValue(jobId, out var job)
            ? job.Snapshot()
            : throw new KeyNotFoundException("The synchronization job was not found.");
    }

    public bool Cancel(Guid jobId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!jobs.TryGetValue(jobId, out var job) || job.Snapshot().State is
            SyncJobState.Completed or SyncJobState.Rejected or SyncJobState.Canceled or SyncJobState.Failed)
        {
            return false;
        }

        job.Cancellation.Cancel();
        return true;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var job in jobs.Values)
        {
            job.Cancellation.Cancel();
        }

        try
        {
            Task.WhenAll(jobs.Values.Select(job => job.Execution ?? Task.CompletedTask))
                .Wait(TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
        }

        foreach (var job in jobs.Values)
        {
            job.Cancellation.Dispose();
        }

        syncGate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task RunAsync(Job job, SyncRequest request)
    {
        try
        {
            if (!await authorizer.AuthorizeAsync(request, job.Cancellation.Token).ConfigureAwait(false))
            {
                Update(job, SyncJobState.Rejected);
                return;
            }

            Update(job, SyncJobState.Running);
            await syncGate.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
            try
            {
                var messages = await source.ReadMessagesAsync(request, job.Cancellation.Token).ConfigureAwait(false);
                job.Cancellation.Token.ThrowIfCancellationRequested();
                var count = index.UpsertMessages(messages);
                Update(job, SyncJobState.Completed, count);
            }
            finally
            {
                syncGate.Release();
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            Update(job, SyncJobState.Canceled);
        }
        catch
        {
            Update(job, SyncJobState.Failed, errorCode: "sync_failed");
        }
    }

    private void Update(Job job, SyncJobState state, int? count = null, string? errorCode = null)
    {
        job.Update(state, count, errorCode);
        Persist(job);
    }

    private void Persist(Job job)
    {
        var snapshot = job.Snapshot();
        index.SaveSyncJob(new IndexSyncJobRecord(
            snapshot.JobId,
            (int)snapshot.State,
            snapshot.CreatedUtc,
            snapshot.UpdatedUtc,
            snapshot.MessageCount,
            snapshot.ErrorCode,
            job.RequestJson));
    }

    private static string SerializeRequest(SyncRequest request) => JsonSerializer.Serialize(new RequestDocument
    {
        AccountId = request.AccountId,
        Conversations = request.Conversations.Select(item => new ConversationDocument
        {
            Type = item.Type,
            Id = item.Id,
            DisplayName = item.DisplayName,
        }).ToArray(),
        StartUtc = request.Range.StartUtc,
        EndUtc = request.Range.EndUtc,
        IncludeForwarded = request.IncludeForwarded,
    });

    private static SyncRequest DeserializeRequest(string json)
    {
        var document = JsonSerializer.Deserialize<RequestDocument>(json) ??
            throw new InvalidDataException("The persisted synchronization request is invalid.");
        return new SyncRequest(
            document.AccountId,
            document.Conversations.Select(item => new ConversationDescriptor(
                document.AccountId,
                item.Type,
                item.Id,
                item.DisplayName)),
            new TimeRange(document.StartUtc, document.EndUtc),
            document.IncludeForwarded);
    }

    private sealed class Job
    {
        private readonly object gate = new();
        private SyncJobState state = SyncJobState.AwaitingAuthorization;
        private DateTimeOffset updatedUtc;
        private int? messageCount;
        private string? errorCode;

        public Job(Guid id, string requestJson)
        {
            Id = id;
            RequestJson = requestJson;
            CreatedUtc = DateTimeOffset.UtcNow;
            updatedUtc = CreatedUtc;
        }

        public Job(IndexSyncJobRecord record)
        {
            Id = record.JobId;
            RequestJson = record.RequestJson;
            CreatedUtc = record.CreatedUtc;
            updatedUtc = record.UpdatedUtc;
            state = Enum.IsDefined(typeof(SyncJobState), record.State)
                ? (SyncJobState)record.State
                : SyncJobState.Failed;
            messageCount = record.MessageCount;
            errorCode = record.ErrorCode;
        }

        public Guid Id { get; }

        public DateTimeOffset CreatedUtc { get; }

        public string RequestJson { get; }

        public CancellationTokenSource Cancellation { get; } = new();

        public Task? Execution { get; set; }

        public void Update(SyncJobState newState, int? count = null, string? errorCode = null)
        {
            lock (gate)
            {
                state = newState;
                messageCount = count;
                this.errorCode = errorCode;
                updatedUtc = DateTimeOffset.UtcNow;
            }
        }

        public SyncJobSnapshot Snapshot()
        {
            lock (gate)
            {
                return new SyncJobSnapshot(Id, state, CreatedUtc, updatedUtc, messageCount, errorCode);
            }
        }
    }

    private sealed class RequestDocument
    {
        public required string AccountId { get; init; }
        public required ConversationDocument[] Conversations { get; init; }
        public required DateTimeOffset StartUtc { get; init; }
        public required DateTimeOffset EndUtc { get; init; }
        public required bool IncludeForwarded { get; init; }
    }

    private sealed class ConversationDocument
    {
        public required ConversationType Type { get; init; }
        public required string Id { get; init; }
        public required string DisplayName { get; init; }
    }
}
