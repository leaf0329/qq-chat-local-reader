using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace QQChatLocalReader.Infrastructure.SnapshotHelper;

internal static partial class PipePeerProcess
{
    public static int GetClientProcessId(NamedPipeServerStream server)
    {
        ArgumentNullException.ThrowIfNull(server);
        if (!GetNamedPipeClientProcessId(server.SafePipeHandle, out var processId))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The snapshot helper client could not be authenticated.");
        }

        return checked((int)processId);
    }

    public static int GetServerProcessId(NamedPipeClientStream client)
    {
        ArgumentNullException.ThrowIfNull(client);
        if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out var processId))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The snapshot helper server could not be authenticated.");
        }

        return checked((int)processId);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeClientProcessId(
        SafePipeHandle pipe,
        out uint clientProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetNamedPipeServerProcessId(
        SafePipeHandle pipe,
        out uint serverProcessId);
}
