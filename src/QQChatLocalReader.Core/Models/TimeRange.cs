namespace QQChatLocalReader.Core.Models;

public sealed record TimeRange
{
    public TimeRange(DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        StartUtc = startUtc.ToUniversalTime();
        EndUtc = endUtc.ToUniversalTime();

        if (StartUtc >= EndUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endUtc), "End must be after start.");
        }
    }

    public DateTimeOffset StartUtc { get; }

    public DateTimeOffset EndUtc { get; }

    public static TimeRange ForLastNaturalDays(
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        int days)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        if (days < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be positive.");
        }

        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var localStart = DateTime.SpecifyKind(
            localNow.Date.AddDays(-(days - 1)),
            DateTimeKind.Unspecified);
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(localStart, timeZone);

        return new TimeRange(startUtc, now);
    }

    public IEnumerable<TimeRange> SplitByMaximumDuration(TimeSpan maximumDuration)
    {
        if (maximumDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumDuration),
                "Maximum duration must be positive.");
        }

        for (var cursor = StartUtc; cursor < EndUtc;)
        {
            var next = cursor + maximumDuration;
            if (next > EndUtc)
            {
                next = EndUtc;
            }

            yield return new TimeRange(cursor, next);
            cursor = next;
        }
    }

    public bool Contains(DateTimeOffset timestamp) =>
        timestamp >= StartUtc && timestamp < EndUtc;
}
