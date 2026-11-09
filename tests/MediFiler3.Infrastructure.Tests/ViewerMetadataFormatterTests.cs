using System.Globalization;
using MediFiler3.Application.Viewing;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ViewerMetadataFormatterTests
{
    [Theory]
    [InlineData(0, "0.0 B")]
    [InlineData(1, "1.0 B")]
    [InlineData(1023, "1023.0 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1536, "1.5 KB")]
    [InlineData(1048576, "1.0 MB")]
    [InlineData(1073741824, "1.0 GB")]
    [InlineData(long.MaxValue, "8.0 EB")]
    public void FormatsSizeWithoutOverflow(long bytes, string expected) =>
        Assert.Equal(expected, ViewerMetadataFormatter.Format(new(bytes), CultureInfo.InvariantCulture));

    [Fact]
    public void ShowsOnlyCompleteResolutionAndClearsAbsentMetadata()
    {
        Assert.Equal("1.0 KB - [3840×2160]",
            ViewerMetadataFormatter.Format(new(1024, 3840, 2160), CultureInfo.InvariantCulture));
        Assert.Equal("1.0 KB", ViewerMetadataFormatter.Format(new(1024, 3840), CultureInfo.InvariantCulture));
        Assert.Equal("1.0 KB", ViewerMetadataFormatter.Format(new(1024, 0, 0), CultureInfo.InvariantCulture));
        Assert.Equal(string.Empty, ViewerMetadataFormatter.Format(null));
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(125, "02:05")]
    [InlineData(3725, "1:02:05")]
    [InlineData(90000, "25:00:00")]
    public void DurationUsesTotalHoursWhenProvidedByAHandler(int seconds, string expected) =>
        Assert.Equal($"1.0 B - {expected}", ViewerMetadataFormatter.Format(
            new(1, Duration: TimeSpan.FromSeconds(seconds)), CultureInfo.InvariantCulture));

    [Fact]
    public void DefaultDecimalSeparatorIsAPeriodRegardlessOfSystemCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sv-SE");
            Assert.Equal("1.5 KB - [128×128]", ViewerMetadataFormatter.Format(new(1536, 128, 128)));
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Fact]
    public void RespectsDecimalCulture() => Assert.Equal("1,5 KB",
        ViewerMetadataFormatter.Format(new(1536), CultureInfo.GetCultureInfo("sv-SE")));
}
