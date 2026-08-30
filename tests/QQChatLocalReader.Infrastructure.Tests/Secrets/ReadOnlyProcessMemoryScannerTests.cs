using System.Runtime.InteropServices;
using System.Text;
using QQChatLocalReader.Infrastructure.Secrets;

namespace QQChatLocalReader.Infrastructure.Tests.Secrets;

public sealed class ReadOnlyProcessMemoryScannerTests
{
    [Fact]
    public void ScanFindsPinnedCandidateInCurrentProcess()
    {
        var expected = Encoding.ASCII.GetBytes("scanner-test-key");
        var marker = new byte[expected.Length + 2];
        expected.CopyTo(marker, 1);
        var pin = GCHandle.Alloc(marker, GCHandleType.Pinned);

        try
        {
            var result = ReadOnlyProcessMemoryScanner.Scan(
                Environment.ProcessId,
                candidate => candidate.SequenceEqual(expected));

            Assert.True(result.MatchFound);
            Assert.True(result.CandidatesVisited > 0);
        }
        finally
        {
            pin.Free();
            GC.KeepAlive(marker);
        }
    }
}
