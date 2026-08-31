using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Security;
using QQChatLocalReader.Infrastructure.QqData;
using QQChatLocalReader.Infrastructure.Snapshots;

namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

public sealed class ElevatedSnapshotClient
{
    private const int ErrorCancelled = 1223;
    private static readonly TimeSpan ConnectionTimeout = TimeSpan.FromSeconds(30);
    private readonly string helperExecutablePath;

    public ElevatedSnapshotClient(string helperExecutablePath)
    {
        this.helperExecutablePath = Path.GetFullPath(helperExecutablePath);
        if (!File.Exists(this.helperExecutablePath))
        {
            throw new FileNotFoundException("The snapshot helper executable was not found.");
        }
    }

    public async Task<DatabaseSnapshot> CreateAsync(
        QqDatabaseSet databaseSet,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(databaseSet);

        var snapshotRoot = SnapshotPathPolicy.GetDefaultSnapshotRoot();
        SnapshotStaleCleanup.DeleteOlderThan(
            snapshotRoot,
            DateTimeOffset.UtcNow.AddDays(-1));
        var pipeName = $"qqclr-snapshot-{Guid.NewGuid():N}";
        await using var server = SecureSnapshotPipe.CreateServer(pipeName);
        using var helper = StartHelper(pipeName);

        using var connectionTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        connectionTimeout.CancelAfter(ConnectionTimeout);
        var connectionTask = server.WaitForConnectionAsync(connectionTimeout.Token);
        var exitTask = helper.WaitForExitAsync(cancellationToken);
        var firstCompleted = await Task.WhenAny(connectionTask, exitTask).ConfigureAwait(false);
        if (firstCompleted == exitTask && !server.IsConnected)
        {
            throw new SnapshotHelperException("The elevated snapshot helper exited before connecting.");
        }

        try
        {
            await connectionTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryTerminateUnconnectedHelper(helper);
            throw new SnapshotHelperException("The elevated snapshot helper did not connect in time.");
        }
        if (PipePeerProcess.GetClientProcessId(server) != helper.Id)
        {
            throw new SecurityException("An unexpected process connected to the snapshot pipe.");
        }

        var request = new SnapshotHelperRequest
        {
            DatabasePath = databaseSet.DatabasePath,
            CompanionPaths = databaseSet.CompanionPaths.ToArray(),
            SnapshotRoot = snapshotRoot,
        };
        await PipeMessageProtocol.WriteAsync(server, request, cancellationToken).ConfigureAwait(false);
        var response = await PipeMessageProtocol
            .ReadAsync<SnapshotHelperResponse>(server, cancellationToken)
            .ConfigureAwait(false);
        await exitTask.ConfigureAwait(false);

        if (!response.Success || helper.ExitCode != 0)
        {
            TryCleanupReturnedSnapshot(snapshotRoot, response);
            throw new SnapshotHelperException(
                $"The elevated snapshot helper failed ({response.ErrorCode ?? "unknown_error"}).");
        }

        try
        {
            return AdoptSnapshot(snapshotRoot, response);
        }
        catch
        {
            TryCleanupReturnedSnapshot(snapshotRoot, response);
            throw;
        }
    }

    private Process StartHelper(string pipeName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = helperExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(helperExecutablePath)!,
            UseShellExecute = true,
            Verb = "runas",
        };
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        startInfo.ArgumentList.Add("--server-pid");
        startInfo.ArgumentList.Add(Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        try
        {
            return Process.Start(startInfo)
                ?? throw new SnapshotHelperException("Windows did not start the elevated snapshot helper.");
        }
        catch (Win32Exception exception) when (exception.NativeErrorCode == ErrorCancelled)
        {
            throw new SnapshotElevationDeniedException();
        }
    }

    private static DatabaseSnapshot AdoptSnapshot(
        string snapshotRoot,
        SnapshotHelperResponse response)
    {
        if (response.DirectoryPath is null || response.DatabasePath is null)
        {
            throw new InvalidDataException("The snapshot helper returned an incomplete result.");
        }

        var root = Path.GetFullPath(snapshotRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetFullPath(response.DirectoryPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directoryInfo = new DirectoryInfo(directory);
        if (!string.Equals(directoryInfo.Parent?.FullName, root, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(directoryInfo.Name, "N", out _) ||
            !directoryInfo.Exists ||
            (directoryInfo.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The snapshot helper returned an invalid directory.");
        }

        var databasePath = ValidateSnapshotFile(directory, response.DatabasePath);
        if (response.CompanionPaths is null || response.CompanionPaths.Length > 9)
        {
            throw new InvalidDataException("The snapshot helper returned invalid companion files.");
        }

        var companions = response.CompanionPaths
            .Select(path => ValidateSnapshotFile(directory, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DatabaseSnapshot(root, directory, databasePath, companions);
    }

    private static string ValidateSnapshotFile(string directory, string path)
    {
        var normalizedPath = Path.GetFullPath(path);
        var file = new FileInfo(normalizedPath);
        if (!string.Equals(file.DirectoryName, directory, StringComparison.OrdinalIgnoreCase) ||
            !file.Exists ||
            (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("The snapshot helper returned an invalid file.");
        }

        return normalizedPath;
    }

    private static void TryCleanupReturnedSnapshot(
        string snapshotRoot,
        SnapshotHelperResponse response)
    {
        if (response.DirectoryPath is null)
        {
            return;
        }

        try
        {
            SnapshotDirectoryCleanup.Delete(snapshotRoot, response.DirectoryPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
        }
    }

    private static void TryTerminateUnconnectedHelper(Process helper)
    {
        try
        {
            if (!helper.HasExited)
            {
                helper.Kill(entireProcessTree: true);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
        }
    }
}
