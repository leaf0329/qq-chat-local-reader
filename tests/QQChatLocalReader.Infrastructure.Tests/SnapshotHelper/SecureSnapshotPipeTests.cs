using QQChatLocalReader.Infrastructure.SnapshotHelper;

namespace QQChatLocalReader.Infrastructure.Tests.SnapshotHelper;

public sealed class SecureSnapshotPipeTests
{
    [Fact]
    public async Task PipeAuthenticatesBothLocalProcessEndpoints()
    {
        var pipeName = $"qqclr-test-{Guid.NewGuid():N}";
        await using var server = SecureSnapshotPipe.CreateServer(pipeName);
        await using var client = SecureSnapshotPipe.CreateClient(pipeName);

        var waitForServer = server.WaitForConnectionAsync();
        await client.ConnectAsync();
        await waitForServer;

        Assert.Equal(Environment.ProcessId, PipePeerProcess.GetClientProcessId(server));
        Assert.Equal(Environment.ProcessId, PipePeerProcess.GetServerProcessId(client));
    }
}
