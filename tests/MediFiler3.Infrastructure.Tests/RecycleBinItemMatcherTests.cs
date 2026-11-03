using MediFiler3.Application.Operations;
using MediFiler3.Infrastructure.Operations;

namespace MediFiler3.Infrastructure.Tests;

public sealed class RecycleBinItemMatcherTests
{
    [Fact]
    public void SelectsOnlyAnExactPathTypeAndIdentityMatch()
    {
        var entries = new[]
        {
            new RecycleBinEntryDescriptor(@"C:\old\file.txt", "VOL:111", false),
            new RecycleBinEntryDescriptor(@"C:\old\file.txt", "VOL:222", false),
            new RecycleBinEntryDescriptor(@"C:\old\file.txt", "VOL:111", true)
        };

        var result = RecycleBinItemMatcher.FindUnique(entries, @"c:\old\FILE.txt", "VOL:222", false);

        Assert.Equal(RecycleBinMatchOutcome.Matched, result.Outcome);
        Assert.Equal(1, result.Index);
    }

    [Fact]
    public void ReportsMissingAmbiguousAndIdentityMismatchWithoutChoosingAnItem()
    {
        var one = new[] { new RecycleBinEntryDescriptor(@"C:\old\file.txt", "VOL:111", false) };
        var duplicate = new[]
        {
            one[0], new RecycleBinEntryDescriptor(@"C:\old\file.txt", "VOL:111", false)
        };

        Assert.Equal(RecycleBinMatchOutcome.NotFound,
            RecycleBinItemMatcher.FindUnique(one, @"C:\elsewhere\file.txt", "VOL:111", false).Outcome);
        Assert.Equal(RecycleBinMatchOutcome.IdentityMismatch,
            RecycleBinItemMatcher.FindUnique(one, @"C:\old\file.txt", "VOL:222", false).Outcome);
        Assert.Equal(RecycleBinMatchOutcome.Ambiguous,
            RecycleBinItemMatcher.FindUnique(duplicate, @"C:\old\file.txt", "VOL:111", false).Outcome);
    }
}
