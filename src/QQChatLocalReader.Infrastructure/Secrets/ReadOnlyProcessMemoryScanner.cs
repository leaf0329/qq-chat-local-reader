using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;

namespace QQChatLocalReader.Infrastructure.Secrets;

public static partial class ReadOnlyProcessMemoryScanner
{
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const int BufferSize = 1024 * 1024;
    private const int BoundarySize = AsciiKeyCandidateExtractor.CandidateLength + 1;

    private static readonly HashSet<uint> ReadableProtections =
    [
        0x02,
        0x04,
        0x08,
        0x20,
        0x40,
        0x80,
    ];

    public static ProcessMemoryScanResult Scan(
        int processId,
        KeyCandidateVisitor visitor,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        ArgumentNullException.ThrowIfNull(visitor);

        using var process = OpenProcess(
            ProcessQueryInformation | ProcessVmRead,
            inheritHandle: false,
            (uint)processId);
        if (process.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "The QQ process could not be opened for read-only inspection.");
        }

        var buffer = new byte[BufferSize];
        var boundary = new byte[BoundarySize * 2];
        var tail = new byte[BoundarySize];
        var candidatesVisited = 0;

        try
        {
            nuint address = 0;
            while (VirtualQueryEx(
                       process,
                       (nint)address,
                       out var information,
                       (nuint)Marshal.SizeOf<MemoryBasicInformation>()) != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var regionAddress = (nuint)information.BaseAddress;
                var nextAddress = regionAddress + information.RegionSize;
                if (nextAddress <= address)
                {
                    break;
                }

                if (IsReadablePrivateRegion(information))
                {
                    var result = ScanRegion(
                        process,
                        regionAddress,
                        information.RegionSize,
                        buffer,
                        boundary,
                        tail,
                        visitor,
                        cancellationToken);
                    candidatesVisited += result.CandidatesVisited;
                    if (result.MatchFound)
                    {
                        return new ProcessMemoryScanResult(candidatesVisited, MatchFound: true);
                    }
                }

                address = nextAddress;
            }

            return new ProcessMemoryScanResult(candidatesVisited, MatchFound: false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
            CryptographicOperations.ZeroMemory(boundary);
            CryptographicOperations.ZeroMemory(tail);
        }
    }

    private static ProcessMemoryScanResult ScanRegion(
        SafeProcessHandle process,
        nuint regionAddress,
        nuint regionSize,
        byte[] buffer,
        byte[] boundary,
        byte[] tail,
        KeyCandidateVisitor visitor,
        CancellationToken cancellationToken)
    {
        nuint offset = 0;
        var tailLength = 0;
        var candidatesVisited = 0;

        while (offset < regionSize)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var remaining = regionSize - offset;
            var requested = (nuint)Math.Min((ulong)buffer.Length, (ulong)remaining);
            if (!ReadProcessMemory(
                    process,
                    (nint)(regionAddress + offset),
                    buffer,
                    requested,
                    out var bytesRead) ||
                bytesRead == 0)
            {
                offset += requested;
                tailLength = 0;
                continue;
            }

            var length = checked((int)bytesRead);
            if (tailLength > 0)
            {
                tail.AsSpan(0, tailLength).CopyTo(boundary);
                var boundaryHeadLength = Math.Min(BoundarySize, length);
                buffer.AsSpan(0, boundaryHeadLength).CopyTo(boundary.AsSpan(tailLength));
                var matched = false;
                candidatesVisited += AsciiKeyCandidateExtractor.VisitCandidates(
                    boundary.AsSpan(0, tailLength + boundaryHeadLength),
                    candidate => matched = visitor(candidate));
                if (matched)
                {
                    return new ProcessMemoryScanResult(candidatesVisited, MatchFound: true);
                }
            }

            var found = false;
            candidatesVisited += AsciiKeyCandidateExtractor.VisitCandidates(
                buffer.AsSpan(0, length),
                candidate => found = visitor(candidate),
                allowCandidateAtStart: tailLength == 0);
            if (found)
            {
                return new ProcessMemoryScanResult(candidatesVisited, MatchFound: true);
            }

            tailLength = Math.Min(BoundarySize, length);
            buffer.AsSpan(length - tailLength, tailLength).CopyTo(tail);
            offset += bytesRead;
        }

        return new ProcessMemoryScanResult(candidatesVisited, MatchFound: false);
    }

    private static bool IsReadablePrivateRegion(MemoryBasicInformation information)
    {
        var baseProtection = information.Protect & 0xff;
        return information.State == MemCommit &&
               information.Type == MemPrivate &&
               (information.Protect & (PageGuard | PageNoAccess)) == 0 &&
               ReadableProtections.Contains(baseProtection);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandle,
        uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadProcessMemory(
        SafeProcessHandle process,
        nint baseAddress,
        [Out] byte[] buffer,
        nuint size,
        out nuint numberOfBytesRead);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nuint VirtualQueryEx(
        SafeProcessHandle process,
        nint address,
        out MemoryBasicInformation buffer,
        nuint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryBasicInformation
    {
        public nint BaseAddress;
        public nint AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public nuint RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }
}
