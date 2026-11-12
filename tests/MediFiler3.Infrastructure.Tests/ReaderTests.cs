using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Infrastructure.Files;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ReaderTests
{
    private static FileEntry File(string name) => new(name, name, 1, DateTime.UnixEpoch);
    [Fact]
    public void ProjectionKeepsSharedOrderAndChoosesNextImageForUnsupportedCurrent()
    {
        var files = new[] { File("1.png"), File("2.txt"), File("3.gif"), File("4.webp"), File("5.mp4") };
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[1], true);
        Assert.Equal(new[] { files[0], files[2], files[3] }, reader.Pages); Assert.Equal(files[2].FullPath, reader.Anchor!.Path);
        reader.SetFiles("folder", files, files[^1], true); Assert.Equal(files[3].FullPath, reader.Anchor!.Path);
        reader.SetFiles("empty", [files[1]], files[1], false); Assert.Empty(reader.Pages); Assert.Null(reader.Anchor);
    }
    [Fact]
    public void GeometryRetainsFileFractionAcrossResizeAndEarlierMetadataCompletion()
    {
        var files = new[] { File("1.png"), File("2.png"), File("3.png") };
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[1], true); reader.Arrange(1000);
        var anchor = reader.Capture(reader.Top(1) + reader.Height(1) * .4);
        reader.SetDimensions(files[0], 100, 20); reader.Arrange(500);
        var offset = reader.Offset(anchor); Assert.Equal(1, reader.IndexAt(offset));
        Assert.Equal(.4, reader.Capture(offset)!.Fraction, 6);
        reader.SetFiles("folder", files, files[0], false); Assert.Equal(anchor!.Path, reader.Anchor!.Path);
    }
    [Fact]
    public void ProjectionPrunesRemovedRecordsAndKeepsWindowBoundedForLargeSequences()
    {
        var files = Enumerable.Range(0, 10000).Select(i => File($"{i}.png")).ToArray();
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[9000], true); reader.Arrange(1000);
        Assert.InRange(reader.Window(reader.Offset(reader.Anchor), 800).Count, 1, 6);
        reader.SetFiles("folder", files.Take(5).ToArray(), files[3], false); Assert.Equal(files[4].FullPath, reader.Anchor!.Path);
    }
    [Fact]
    public void RemovalUsesNextSurvivingImageThenPreviousAndPreservesAnchorOnInsertions()
    {
        var a = File("a.png"); var b = File("b.png"); var c = File("c.png"); var note = File("b.txt");
        var reader = new ReaderSessionState(); reader.SetFiles("folder", [a, b, note, c], b, true); reader.Arrange(200);
        var anchor = reader.Capture(reader.Top(1) + reader.Height(1) * .3);
        reader.SetFiles("folder", [File("0.png"), a, b, note, c], a, false);
        Assert.Equal(anchor, reader.Anchor);
        reader.SetFiles("folder", [a, note, c], a, false); Assert.Equal(c.FullPath, reader.Anchor!.Path);
        reader.SetFiles("folder", [a, note], note, false); Assert.Equal(a.FullPath, reader.Anchor!.Path);
        reader.SetFiles("folder", [note], note, false); Assert.Null(reader.Anchor);
    }
    [Fact]
    public void KnownFileAndFolderMappingsRetainFractionWithoutCrossingSiblingPrefixes()
    {
        var old = File(System.IO.Path.Combine("root", "a", "page.png"));
        var renamed = old with { FullPath = System.IO.Path.Combine("root", "b", "renamed.png"), Name = "renamed.png" };
        var reader = new ReaderSessionState(); var folder = System.IO.Path.Combine("root", "a");
        reader.SetFiles(folder, [old], old, true); reader.SetDimensions(old, 100, 800); reader.Arrange(100);
        reader.Capture(reader.Height(0) * .4);
        reader.MapPaths([(old.FullPath, System.IO.Path.Combine(folder, "renamed.png")), (folder, System.IO.Path.Combine("root", "b"))]);
        reader.SetFiles(System.IO.Path.Combine("root", "b"), [renamed], renamed, false);
        Assert.Equal(renamed.FullPath, reader.Anchor!.Path); Assert.Equal(.4, reader.Anchor.Fraction, 6); Assert.Equal(800, reader.Height(0));
        reader.MapPaths([(System.IO.Path.Combine("root", "bb"), "elsewhere")]);
        Assert.Equal(renamed.FullPath, reader.Anchor.Path);
    }
    [Fact]
    public void ReaderModeClearsGallerySelectionWithoutChangingRootsFolderOrFiles()
    {
        var state = new NavigationSessionState(); var root = new MediFiler3.Application.Folders.FolderTreeEntry("root", "root");
        var files = new[] { File("1.png"), File("2.png") };
        state.ReplaceRoots([root]); state.SetActiveFolder(root); state.ReplaceFiles(files);
        state.GallerySelection.SelectAll(state.Files); state.SwitchViewingMode(ViewingMode.Reader);
        Assert.Same(root, state.ActiveRoot); Assert.Same(root, state.ActiveFolder); Assert.Equal(files, state.Files); Assert.Equal(0, state.GallerySelection.Count);
    }
    [Fact]
    public void ScalingSpacingAndGapAnchorsUseImageGeometryAndClampWidePages()
    {
        var files = new[] { File("1.png"), File("2.png"), File("3.png") };
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[1], true);
        reader.SetDimensions(files[0], 100, 300); reader.SetDimensions(files[1], 400, 100);
        reader.Arrange(1000, 600);
        var anchor = reader.Capture(reader.Top(1) + reader.Height(1) * .4);
        reader.SetOptions(new(ReaderScalingMode.FitHeight, 24, 0, false));
        Assert.Equal(600, reader.Height(0)); Assert.Equal(200, reader.Width(0));
        Assert.Equal(250, reader.Height(1)); Assert.Equal(1000, reader.Width(1));
        Assert.Equal(624, reader.Top(1));
        Assert.Equal(anchor, reader.Capture(reader.Offset(anchor)));
        var gap = reader.Capture(reader.Height(0) + 12);
        Assert.Equal(1, gap!.Fraction); Assert.Equal(.5, gap.GapFraction);
        reader.SetOptions(reader.Options with { Spacing = 64 });
        Assert.Equal(reader.Height(0) + 32, reader.Offset(gap));
        Assert.Equal(reader.Top(2) + reader.Height(2), reader.Extent); // No trailing gap.
    }
    [Fact]
    public void PreloadChangesBoundRealizationWithoutChangingTheAnchor()
    {
        var files = Enumerable.Range(0, 100).Select(i => File($"{i}.png")).ToArray();
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[40], true); reader.Arrange(100, 100);
        var anchor = reader.Capture(reader.Top(40) + 20);
        reader.SetOptions(new(Preload: 0)); var none = reader.Window(reader.Offset(anchor), 100);
        reader.SetOptions(new(Preload: 6)); var more = reader.Window(reader.Offset(anchor), 100);
        Assert.Equal(none.Count + 6, more.Count); Assert.Equal(anchor, reader.Anchor);
        Assert.Equal(39, none[0]); Assert.Equal(40, none[^1]);
    }
    [Theory]
    [InlineData(-1, 0, 2)]
    [InlineData(99, 0, 2)]
    [InlineData(0, -1, 2)]
    [InlineData(0, 65, 2)]
    [InlineData(0, 3, 2)]
    [InlineData(0, 0, -1)]
    [InlineData(0, 0, 7)]
    public void ReaderSettingsRejectInvalidScalingSpacingAndPreload(int scaling, int spacing, int preload)
    {
        var options = new ReaderOptions((ReaderScalingMode)scaling, spacing, preload);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediFiler3.Application.Persistence.ApplicationSettings().WithReaderOptions(options).Validate());
    }
    [Fact]
    public async Task ReaderSettingsLoadLegacyDefaultsAndPersistAlongsideExistingSettings()
    {
        var legacy = System.Text.Json.JsonSerializer.Deserialize<MediFiler3.Application.Persistence.ApplicationSettings>(
            "{\"Muted\":true,\"Volume\":0.73,\"CarouselEnabled\":false,\"GalleryThumbnailWidth\":200}")!;
        Assert.Equal(new ReaderOptions(), legacy.GetReaderOptions()); legacy.Validate();
        using var db = new TemporaryDatabase();
        var store = new MediFiler3.Infrastructure.Persistence.SqliteApplicationStateStore(db.Database);
        await store.SaveAsync(legacy);
        var settings = new MediFiler3.Application.Persistence.ApplicationSettingsService(store); await settings.InitializeAsync();
        var options = new ReaderOptions(ReaderScalingMode.FitHeight, 64, 6, false, 37);
        settings.Update(value => value.WithReaderOptions(options)); await settings.FlushAsync();
        var restarted = new MediFiler3.Application.Persistence.ApplicationSettingsService(
            new MediFiler3.Infrastructure.Persistence.SqliteApplicationStateStore(new MediFiler3.Infrastructure.Persistence.SqliteDatabase(db.DatabasePath)));
        await restarted.InitializeAsync();
        Assert.Equal(legacy.WithReaderOptions(options), restarted.Current);
    }
    [Theory]
    [InlineData(10)]
    [InlineData(50)]
    [InlineData(100)]
    public void WidthPercentageScalesTheImageColumnAndPreservesTheAnchor(int percent)
    {
        var files = new[] { File("1.png"), File("2.png"), File("3.png") };
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[1], true);
        reader.SetDimensions(files[1], 100, 200); reader.Arrange(1000, 600);
        var anchor = reader.Capture(reader.Top(1) + reader.Height(1) * .3);
        reader.SetOptions(new(WidthPercent: percent));
        Assert.Equal(percent * 10, reader.Width(1)); Assert.Equal(percent * 20, reader.Height(1));
        Assert.Equal(anchor, reader.Capture(reader.Offset(anchor)));
        reader.SetOptions(reader.Options with { Scaling = ReaderScalingMode.FitHeight });
        Assert.Equal(600, reader.Height(1)); Assert.Equal(300, reader.Width(1));
        reader.SetOptions(reader.Options with { Scaling = ReaderScalingMode.FitWidth });
        Assert.Equal(percent * 10, reader.Width(1));
    }
    [Theory]
    [InlineData(9)]
    [InlineData(101)]
    public void WidthPercentageRejectsValuesOutsideTheToolbarRange(int percent)
    {
        var options = new ReaderOptions(WidthPercent: percent);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new MediFiler3.Application.Persistence.ApplicationSettings().WithReaderOptions(options).Validate());
    }
    [Theory]
    [InlineData("1", 0)]
    [InlineData(" 2 ", 1)]
    [InlineData("PAGE2.PNG", -1)]
    [InlineData("page3.png", -1)]
    [InlineData("0", -1)]
    [InlineData("4", -1)]
    [InlineData("", -1)]
    [InlineData("missing.png", -1)]
    [InlineData("../../page2.png", -1)]
    public void PageLookupAcceptsOnlyPageNumbersInTheProjection(string query, int expected)
    {
        var pages = new[] { File("page1.png"), File("page2.png"), File("page3.png") };
        Assert.Equal(expected, ReaderPageLookup.Resolve(pages, query, out var error));
        Assert.Equal(expected >= 0, error.Length == 0);
        Assert.Equal(-1, ReaderPageLookup.Resolve([], query, out _));
    }
    [Fact]
    public void PageLookupRejectsFilenameAndFullPathSearch()
    {
        var files = new[] { new FileEntry("folder1/page.png", "page.png", 1, DateTime.UnixEpoch), new FileEntry("folder2/page.png", "page.png", 1, DateTime.UnixEpoch) };
        Assert.Equal(-1, ReaderPageLookup.Resolve(files, "page.png", out _));
        Assert.Equal(-1, ReaderPageLookup.Resolve(files, "FOLDER2/PAGE.PNG", out _));
        Assert.Equal(1, ReaderPageLookup.Resolve(files, "2", out _));
    }
    [Fact]
    public void ReaderNavigationClampsWithinTheSequenceAndRetainsExplicitTargetsAcrossGeometryChanges()
    {
        var files = Enumerable.Range(0, 10000).Select(i => File($"{i}.png")).ToArray();
        var reader = new ReaderSessionState(); reader.SetFiles("folder", files, files[0], true); reader.Arrange(1000, 600);
        Assert.False(reader.SelectPage(-1)); Assert.False(reader.SelectPage(files.Length));
        Assert.True(reader.Navigate(ReaderNavigation.PreviousImage, 600, 0)); Assert.Equal(files[0].FullPath, reader.Anchor!.Path);
        Assert.True(reader.SelectPage(9000)); reader.SetDimensions(files[0], 10, 3);
        Assert.Equal(files[9000].FullPath, reader.Capture(reader.Offset(reader.Anchor))!.Path);
        reader.Navigate(ReaderNavigation.NextImage, 600, 9999); Assert.Equal(files[^1].FullPath, reader.Anchor!.Path);
        reader.Navigate(ReaderNavigation.Start, 600, 9999); Assert.Equal(0, reader.Offset(reader.Anchor));
        reader.Navigate(ReaderNavigation.LineDown, 600, 0); Assert.Equal(48, reader.Offset(reader.Anchor), 5);
        reader.Navigate(ReaderNavigation.ViewDown, 600, 0); Assert.Equal(588, reader.Offset(reader.Anchor), 5);
        reader.Navigate(ReaderNavigation.ViewUp, 600, 0); Assert.Equal(48, reader.Offset(reader.Anchor), 5);
        reader.Navigate(ReaderNavigation.LineUp, 600, 0); Assert.Equal(0, reader.Offset(reader.Anchor));
        reader.Navigate(ReaderNavigation.End, 600, 0); Assert.Equal(reader.Extent - 600, reader.Offset(reader.Anchor), 5);
        Assert.False(new ReaderSessionState().Navigate(ReaderNavigation.End, 600, 0));
    }
    private static void Bmp(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 200; pixels[i + 3] = 255; }
        var encoder = new BmpBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var stream = System.IO.File.Create(path); encoder.Save(stream);
    }
    [Fact]
    public async Task SourceDecodeBoundsPixelsPreservesDimensionsAndReleasesFile()
    {
        using var db = new TemporaryDatabase(); var path = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "page.bmp"); Bmp(path, 3000, 2000);
        var info = new FileInfo(path); var entry = new FileEntry(path, info.Name, info.Length, info.LastWriteTimeUtc);
        var service = new FileSystemReaderImageService(new FileSystemFileContentService());
        var image = await service.DecodeAsync(entry, 10000, default);
        Assert.Equal(3000, image.OriginalWidth); Assert.Equal(2000, image.OriginalHeight);
        Assert.True((long)image.Width * image.Height <= FileSystemReaderImageService.MaximumPixels);
        Assert.Equal((long)image.Width * image.Height * 4, image.Pixels.LongLength);
        using var exclusive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }
    [Fact]
    public async Task CancelledQueuedDecodeDoesNotOpenAnotherSource()
    {
        using var db = new TemporaryDatabase(); var path = Path.Combine(Path.GetDirectoryName(db.DatabasePath)!, "page.bmp"); Bmp(path, 20, 10);
        var content = new BlockingContent(await System.IO.File.ReadAllBytesAsync(path));
        var service = new FileSystemReaderImageService(content); var entry = File(path);
        var first = service.DecodeAsync(entry, 100, default); var second = service.DecodeAsync(entry, 100, default);
        await content.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
        using var stop = new CancellationTokenSource(); var third = service.DecodeAsync(entry, 100, stop.Token); stop.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => third);
        content.Release.TrySetResult(); await Task.WhenAll(first, second); Assert.Equal(2, content.Calls);
    }
    private sealed class BlockingContent(byte[] bytes) : IFileContentService
    {
        public int Calls;
        public readonly TaskCompletionSource Ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Stream> OpenReadAsync(string path, CancellationToken token)
        {
            if (Interlocked.Increment(ref Calls) == 2) Ready.TrySetResult();
            await Release.Task.WaitAsync(token); return new MemoryStream(bytes, false);
        }
    }
}
