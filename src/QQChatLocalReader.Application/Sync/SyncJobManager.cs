using System.Collections.Concurrent;
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
    }

    public Guid Start(SyncRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(request);
        var job = new Job(Guid.NewGuid());
        if (!jobs.TryAdd(job.Id, job))
        {
            throw new InvalidOperationException("A unique synchronization job could not be created.");
        }

        job.Execution = RunAsync(job, request);
        return job.Id;
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
                job.Update(SyncJobState.Rejected);
                return;
            }

            job.Update(SyncJobState.Running);
            await syncGate.WaitAsync(job.Cancellation.Token).ConfigureAwait(false);
            try
            {
                var messages = await source.ReadMessagesAsync(request, job.Cancellation.Token).ConfigureAwait(false);
                job.Cancellation.Token.ThrowIfCancellationRequested();
                var count = index.UpsertMessages(messages);
                job.Update(SyncJobState.Completed, count);
            }
            finally
            {
                syncGate.Release();
            }
        }
        catch (OperationCanceledException) when (job.Cancellation.IsCancellationRequested)
        {
            job.Update(SyncJobState.Canceled);
        }
        catch
        {
            job.Update(SyncJobState.Failed, errorCode: "sync_failed");
        }
    }

    private sealed class Job
    {
        private readonly object gate = new();
        private SyncJobState state = SyncJobState.AwaitingAuthorization;
        private DateTimeOffset updatedUtc;
        private int? messageCount;
        private string? errorCode;

        public Job(Guid id)
        {
            Id = id;
            CreatedUtc = DateTimeOffset.UtcNow;
            updatedUtc = CreatedUtc;
        }

        public Guid Id { get; }

        public DateTimeOffset CreatedUtc { get; }

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
}
