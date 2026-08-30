using QQChatLocalReader.Infrastructure.SnapshotHelper;

namespace QQChatLocalReader.Infrastructure.Tests.SnapshotHelper;

public sealed class PipeMessageProtocolTests
{
    [Fact]
    public async Task RoundTripPreservesMessageWithoutExposingPathsInToString()
    {
        var request = new SnapshotHelperRequest
        {
            DatabasePath = @"C:\private\nt_msg.db",
            CompanionPaths = [@"C:\private\nt_msg.db-wal"],
            SnapshotRoot = @"C:\private\snapshots",
        };
        await using var stream = new MemoryStream();

        await PipeMessageProtocol.WriteAsync(stream, request);
        stream.Position = 0;
        var result = await PipeMessageProtocol.ReadAsync<SnapshotHelperRequest>(stream);

        Assert.Equal(request.DatabasePath, result.DatabasePath);
        Assert.Equal(request.CompanionPaths, result.CompanionPaths);
        Assert.DoesNotContain("private", result.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteAsyncRejectsOversizedMessage()
    {
        var request = new SnapshotHelperRequest
        {
            DatabasePath = new string('x', 70 * 1024),
            CompanionPaths = [],
            SnapshotRoot = @"C:\snapshots",
        };
        await using var stream = new MemoryStream();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            PipeMessageProtocol.WriteAsync(stream, request));
    }
}
