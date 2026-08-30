using System.Text;
using QQChatLocalReader.Infrastructure.Secrets;

namespace QQChatLocalReader.Infrastructure.Tests.Secrets;

public sealed class AsciiKeyCandidateExtractorTests
{
    [Fact]
    public void VisitCandidatesFindsBoundedSixteenByteValue()
    {
        var data = Encoding.ASCII.GetBytes("\0abcdefghijklmnop\0tail");
        byte[]? found = null;

        var count = AsciiKeyCandidateExtractor.VisitCandidates(data, candidate =>
        {
            found = candidate.ToArray();
            return true;
        });

        Assert.Equal(1, count);
        Assert.Equal("abcdefghijklmnop", Encoding.ASCII.GetString(found!));
    }

    [Theory]
    [InlineData("Xabcdefghijklmnop\0")]
    [InlineData("\0abcdefghijklmno\0")]
    [InlineData("\0abcdefghijklmnopq\0")]
    public void VisitCandidatesRejectsUnboundedOrWrongLengthValues(string value)
    {
        var count = AsciiKeyCandidateExtractor.VisitCandidates(
            Encoding.ASCII.GetBytes(value),
            _ => false);

        Assert.Equal(0, count);
    }

    [Fact]
    public void VisitCandidatesCanRejectUnknownLeadingBoundary()
    {
        var data = Encoding.ASCII.GetBytes("abcdefghijklmnop\0");

        var count = AsciiKeyCandidateExtractor.VisitCandidates(
            data,
            _ => false,
            allowCandidateAtStart: false);

        Assert.Equal(0, count);
    }
}
