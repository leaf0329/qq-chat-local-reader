namespace QQChatLocalReader.Infrastructure.Secrets;

public static class AsciiKeyCandidateExtractor
{
    public const int CandidateLength = 16;

    public static int VisitCandidates(
        ReadOnlySpan<byte> data,
        KeyCandidateVisitor visitor,
        bool allowCandidateAtStart = true)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        var visited = 0;
        for (var index = 0; index + CandidateLength < data.Length; index++)
        {
            if ((index > 0 && IsPrintable(data[index - 1])) ||
                (index == 0 && !allowCandidateAtStart) ||
                data[index + CandidateLength] != 0)
            {
                continue;
            }

            var candidate = data.Slice(index, CandidateLength);
            if (!IsCandidate(candidate))
            {
                continue;
            }

            visited++;
            if (visitor(candidate))
            {
                return visited;
            }

            index += CandidateLength;
        }

        return visited;
    }

    private static bool IsCandidate(ReadOnlySpan<byte> candidate)
    {
        foreach (var value in candidate)
        {
            if (!IsPrintable(value))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPrintable(byte value) => value is >= 0x20 and <= 0x7e;
}
