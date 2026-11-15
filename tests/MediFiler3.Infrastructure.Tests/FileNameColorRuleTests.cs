using System.Text.Json;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FileNameColorRuleTests
{
    [Theory]
    [InlineData("page [SET].png", true)] [InlineData("page [set].PSD", true)]
    [InlineData("page SET.png", false)] [InlineData("plain.png", false)]
    public void NamesMatchLiteralTextIgnoringCase(string name, bool match)
    {
        var rule = new FileNameColorRule("[SET]", new(255, 113, 206));
        Assert.Equal(match, FileNameColorRule.Match(name, [rule]) is not null);
    }
    [Fact]
    public void FirstEnabledColorWinsAndExtensionIsPartOfName()
    {
        var pink = new FileNameColorRule(".psd", new(255, 113, 206));
        var blue = new FileNameColorRule("page", new(0, 100, 255));
        Assert.Equal(pink.Color, FileNameColorRule.Match("page.PSD", [pink, blue]));
        Assert.Equal(blue.Color, FileNameColorRule.Match("page.PSD", [blue, pink]));
        Assert.Equal(blue.Color, FileNameColorRule.Match("page.PSD", [pink with { Enabled = false }, blue]));
        Assert.Null(FileNameColorRule.Match("other.jpg", [pink, blue]));
        using var stop = new CancellationTokenSource(); stop.Cancel();
        Assert.Throws<OperationCanceledException>(() => FileNameColorRule.Match("page.PSD", [pink], stop.Token));
    }
    [Fact]
    public void OrderedRulesRoundTripAndOldSettingsHaveNoFileColors()
    {
        FileNameColorRule[] rules = [new("[SET]", new(255, 113, 206)), new(".ico", new(20, 160, 240), false)];
        var settings = new ApplicationSettings(FileNameRules: rules); settings.Validate();
        var restored = JsonSerializer.Deserialize<ApplicationSettings>(JsonSerializer.Serialize(settings))!;
        restored.Validate(); Assert.Equal(rules, restored.FileNameRules);
        Assert.Null(JsonSerializer.Deserialize<ApplicationSettings>("{}")!.FileNameRules);
    }
    [Fact]
    public void InvalidOrDuplicateRulesAreRejected()
    {
        var rule = new FileNameColorRule("name", new(10, 20, 30)); rule.Validate();
        Assert.Throws<ArgumentException>(() => (rule with { Contains = " " }).Validate());
        Assert.Throws<ArgumentException>(() => (rule with { Contains = new string('a', 257) }).Validate());
        Assert.Throws<ArgumentException>(() => (rule with { Id = Guid.Empty }).Validate());
        Assert.Throws<ArgumentException>(() => FileNameColorRule.ValidateAll([rule, rule]));
        Assert.Throws<ArgumentException>(() => FileNameColorRule.ValidateAll(Enumerable.Range(0, 257).Select(i => rule with { Id = Guid.NewGuid() }).ToArray()));
    }
}
