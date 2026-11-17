using MediFiler3.Application.Files;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FileQueryTests
{
    private static FileEntry Entry(string name, int size = 0, int day = 1) =>
        new(Path.Combine("root", name), name, size, new DateTime(2026, 1, 1))
        { CreatedUtc = new DateTime(2026, 1, day) };

    [Fact]
    public void LiteralSearchAndIncludesAndExcludesCombineWithCategoryUnion()
    {
        var image = Entry("MY [SET] 2.png");
        var text = Entry("my [set] 3.txt");
        FileEntry[] files = [image, text, Entry("my [set] 4.mp4"), Entry("my [set] draft.png"), Entry("my new [set].png")];
        var query = new FileQuery("my [set]", ".", "draft", FileCategories.Images | FileCategories.Text);

        Assert.Equal([image, text], query.Apply(files, CancellationToken.None));
        Assert.Empty((query with { Search = "root" }).Apply(files, CancellationToken.None));
    }

    [Fact]
    public void SortPreservesNaturalOrderOfEqualKeysAndUsesCreationMetadata()
    {
        var first = Entry("page 2.jpg", 4, 2);
        var second = Entry("page 10.jpg", 4, 3);
        var third = Entry("page 20.jpg", 12, 1);
        FileEntry[] inventory = [first, second, third];

        Assert.Same(inventory, FileQuery.AllFiles.Apply(inventory, CancellationToken.None));
        Assert.Equal([third, second, first], new FileQuery(Descending: true).Apply(inventory, CancellationToken.None));
        Assert.Equal([third, first, second], new FileQuery(Sort: FileSort.Size, Descending: true).Apply(inventory, CancellationToken.None));
        Assert.Equal([third, first, second], new FileQuery(Sort: FileSort.Created).Apply(inventory, CancellationToken.None));
        Assert.Equal(inventory, new FileQuery(Sort: FileSort.Modified, Descending: true).Apply(inventory, CancellationToken.None));
    }

    [Fact]
    public void RoutingIncludesExtensionlessTextAndUnknownFiles()
    {
        var text = Entry("LICENSE");
        var audio = Entry("track.OPUS");
        var other = Entry("scene.blend");
        FileEntry[] inventory = [text, audio, other];

        Assert.Equal([text, other], new FileQuery(Categories: FileCategories.Text | FileCategories.Other).Apply(inventory, CancellationToken.None));
        Assert.Equal([audio], new FileQuery(Categories: FileCategories.Audio).Apply(inventory, CancellationToken.None));
        Assert.Throws<OperationCanceledException>(() => FileQuery.AllFiles.Apply(inventory, new CancellationToken(true)));
    }

    [Fact]
    public void SavedFiltersValidateLimitsAndRecentReferences()
    {
        var id = Guid.NewGuid();
        var filter = new SavedFileFilter(id, "Images", new FileQuery(Categories: FileCategories.Images));
        SavedFileFilter.Validate([filter], [id]);

        Assert.Throws<InvalidDataException>(() => SavedFileFilter.Validate([filter], [Guid.NewGuid()]));
        Assert.Throws<InvalidDataException>(() => SavedFileFilter.Validate([filter, filter with { Id = Guid.NewGuid(), Name = "IMAGES" }], []));
        Assert.Throws<InvalidDataException>(() => (filter.Query with { Search = new string('a', 257) }).Validate());
    }
}
