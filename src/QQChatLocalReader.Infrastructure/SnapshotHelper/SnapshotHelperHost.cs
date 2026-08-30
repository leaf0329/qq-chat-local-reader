using System.IO.Pipes;
using QQChatLocalReader.Infrastructure.Snapshots;

namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

public static class SnapshotHelperHost
{
    private const int ConnectionTimeoutMilliseconds = 15_000;

    public static async Task<int> RunAsync(
        string[] arguments,
        CancellationToken cancellationToken = default)
    {
        if (!TryParseArguments(arguments, out var pipeName, out var expectedServerProcessId))
        {
            return 2;
        }

        await using var client = SecureSnapshotPipe.CreateClient(pipeName);
        try
        {
            await client
                .ConnectAsync(ConnectionTimeoutMilliseconds, cancellationToken)
                .ConfigureAwait(false);
            if (PipePeerProcess.GetServerProcessId(client) != expectedServerProcessId)
            {
                return 3;
            }

            var request = await PipeMessageProtocol
                .ReadAsync<SnapshotHelperRequest>(client, cancellationToken)
                .ConfigureAwait(false);
            return await ProcessRequestAsync(client, request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            return 4;
        }
    }

    private static async Task<int> ProcessRequestAsync(
        PipeStream pipe,
        SnapshotHelperRequest request,
        CancellationToken cancellationToken)
    {
        DatabaseSnapshot? snapshot = null;
        try
        {
            var databaseSet = await SnapshotPathPolicy
                .ValidateRequestAsync(request, cancellationToken)
                .ConfigureAwait(false);
            var provider = new VssDatabaseSnapshotProvider(
                new WmiShadowCopyService(),
                request.SnapshotRoot);
            snapshot = await provider
                .CreateAsync(databaseSet, cancellationToken)
                .ConfigureAwait(false);

            var response = new SnapshotHelperResponse
            {
                Success = true,
                DirectoryPath = snapshot.DirectoryPath,
                DatabasePath = snapshot.DatabasePath,
                CompanionPaths = snapshot.CompanionPaths.ToArray(),
            };
            await PipeMessageProtocol.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            snapshot = null;
            return 0;
        }
        catch (Exception exception)
        {
            var response = new SnapshotHelperResponse
            {
                Success = false,
                ErrorCode = MapErrorCode(exception),
            };

            try
            {
                await PipeMessageProtocol.WriteAsync(pipe, response, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception communicationException) when (
                communicationException is IOException or OperationCanceledException)
            {
                return 5;
            }

            return 6;
        }
        finally
        {
            if (snapshot is not null)
            {
                await snapshot.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static string MapErrorCode(Exception exception) => exception switch
    {
        InvalidDataException or ArgumentException => "invalid_request",
        ShadowCopyException => "snapshot_failed",
        OperationCanceledException => "cancelled",
        _ => "internal_error",
    };

    private static bool TryParseArguments(
        string[] arguments,
        out string pipeName,
        out int serverProcessId)
    {
        pipeName = string.Empty;
        serverProcessId = 0;
        if (arguments.Length != 4 ||
            !arguments[0].Equals("--pipe", StringComparison.Ordinal) ||
            !arguments[2].Equals("--server-pid", StringComparison.Ordinal) ||
            arguments[1].Length is < 20 or > 80 ||
            arguments[1].Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-') ||
            !int.TryParse(arguments[3], out serverProcessId) ||
            serverProcessId <= 0)
        {
            return false;
        }

        pipeName = arguments[1];
        return true;
    }
}
