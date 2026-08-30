using QQChatLocalReader.Core.Models;

namespace QQChatLocalReader.Core.Tests.Models;

public sealed class TimeRangeTests
{
    [Fact]
    public void LastNaturalDaysStartsAtLocalMidnightSixDaysBeforeToday()
    {
        var timeZone = TimeZoneInfo.CreateCustomTimeZone(
            "UTC+08-test",
            TimeSpan.FromHours(8),
            "UTC+08-test",
            "UTC+08-test");
        var now = new DateTimeOffset(2026, 8, 30, 13, 20, 0, TimeSpan.FromHours(8));

        var range = TimeRange.ForLastNaturalDays(now, timeZone, 7);

        Assert.Equal(
            new DateTimeOffset(2026, 8, 24, 0, 0, 0, TimeSpan.FromHours(8)),
            TimeZoneInfo.ConvertTime(range.StartUtc, timeZone));
        Assert.Equal(now.ToUniversalTime(), range.EndUtc);
    }

    [Fact]
    public void ContainsUsesExclusiveEndBoundary()
    {
        var range = new TimeRange(
            new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero));

        Assert.True(range.Contains(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero)));
        Assert.False(range.Contains(new DateTimeOffset(2026, 8, 2, 0, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void SplitProducesAdjacentNonOverlappingRanges()
    {
        var range = new TimeRange(
            new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 15, 0, 0, 0, TimeSpan.Zero));

        var batches = range.SplitByMaximumDuration(TimeSpan.FromDays(31)).ToArray();

        Assert.Equal(3, batches.Length);
        Assert.Equal(range.StartUtc, batches[0].StartUtc);
        Assert.Equal(batches[0].EndUtc, batches[1].StartUtc);
        Assert.Equal(batches[1].EndUtc, batches[2].StartUtc);
        Assert.Equal(range.EndUtc, batches[2].EndUtc);
    }
}
