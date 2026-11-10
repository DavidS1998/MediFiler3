using MediFiler3.Application.Folders;

namespace MediFiler3.Infrastructure.Tests;

public sealed class RootVisitTimeFormatterTests
{
    [Theory]
    [InlineData(-5, "0 seconds ago")]
    [InlineData(0, "0 seconds ago")]
    [InlineData(1, "1 second ago")]
    [InlineData(5, "5 seconds ago")]
    [InlineData(59, "59 seconds ago")]
    [InlineData(60, "1 minute ago")]
    [InlineData(119, "1 minute ago")]
    [InlineData(120, "2 minutes ago")]
    [InlineData(3599, "59 minutes ago")]
    [InlineData(3600, "1 hour ago")]
    [InlineData(7200, "2 hours ago")]
    [InlineData(86399, "23 hours ago")]
    [InlineData(86400, "1 day ago")]
    [InlineData(172800, "2 days ago")]
    [InlineData(2591999, "29 days ago")]
    [InlineData(2592000, "1 month ago")]
    [InlineData(5184000, "2 months ago")]
    [InlineData(31535999, "12 months ago")]
    [InlineData(31536000, "1 year ago")]
    [InlineData(63072000, "2 years ago")]
    public void FormatsRelativeTimeAtUnitBoundaries(int seconds, string expected)
    {
        var now = new DateTimeOffset(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(expected, RootVisitTimeFormatter.Relative(now.AddSeconds(-seconds), now));
    }

    [Fact]
    public void HandlesMissingTimestampsAndTimezoneOffsets()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("Never visited", RootVisitTimeFormatter.Relative(null, now));
        Assert.Equal("5 seconds ago", RootVisitTimeFormatter.Relative(now.AddSeconds(-5).ToOffset(TimeSpan.FromHours(2)), now));
    }
}
