using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ViewerCoordinatorTests
{
    private static FileEntry File(string name) => new(name, name, 123, DateTime.UtcNow);

    [Fact]
    public async Task RoutesHandlersAndExposesOnlySupportedCapabilities()
    {
        var image = new Handler(file => file.Extension == ".png", ViewerCapabilities.Zoom,
            (_, _) => Task.FromResult(new ViewerContent<string>("image", new(123, 200, 100))));
        var fallback = new Handler(_ => true, ViewerCapabilities.None,
            (_, _) => Task.FromResult(new ViewerContent<string>(null, new(123), true)));
        using var coordinator = new ViewerCoordinator<string>([image, fallback]);
        Assert.Equal(ViewerCapabilities.None, coordinator.GetCapabilities(null));
        Assert.Equal(ViewerCapabilities.Zoom, coordinator.GetCapabilities(File("page.png")));
        var result = await coordinator.LoadAsync(File("page.png"), 150, 150);
        Assert.Equal(200, result!.Metadata.PixelWidth);
        Assert.Equal("image", coordinator.Content!.Presentation);
        var unsupported = await coordinator.LoadAsync(File("file.xyz"), 150, 150);
        Assert.True(unsupported!.IsThumbnailFallback);
        Assert.Equal(ViewerCapabilities.None, coordinator.Capabilities);
        Assert.Null(coordinator.Content!.Presentation);
    }

    [Fact]
    public async Task NewLoadCancelsPreviousAndRejectsLateSuccessEvenForSameFile()
    {
        var first = new TaskCompletionSource<ViewerContent<string>>();
        var second = new TaskCompletionSource<ViewerContent<string>>();
        var tokens = new List<CancellationToken>();
        var handler = new Handler(_ => true, ViewerCapabilities.Zoom, (_, token) =>
        {
            tokens.Add(token);
            return tokens.Count == 1 ? first.Task : second.Task;
        });
        using var coordinator = new ViewerCoordinator<string>([handler]);
        var file = File("image.png");
        var old = coordinator.LoadAsync(file, 100, 100);
        var current = coordinator.LoadAsync(file, 200, 200);
        Assert.True(tokens[0].IsCancellationRequested);
        second.SetResult(new("new", new(123, 3840, 2160)));
        await current;
        first.SetResult(new("old", new(123, 640, 480)));
        Assert.Null(await old);
        Assert.Equal("new", coordinator.Content!.Presentation);
        Assert.Equal("123.0 B - [3840×2160]",
            ViewerMetadataFormatter.Format(coordinator.Content.Metadata, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task CancelReleasesPresentationAndRejectsLateFailure()
    {
        var completion = new TaskCompletionSource<ViewerContent<string>>();
        using var coordinator = new ViewerCoordinator<string>([
            new Handler(_ => true, ViewerCapabilities.Zoom, (_, _) => completion.Task)]);
        var load = coordinator.LoadAsync(File("image.png"), 100, 100);
        coordinator.Cancel();
        completion.SetException(new IOException("superseded"));
        Assert.Null(await load);
        Assert.Null(coordinator.CurrentFile);
        Assert.Null(coordinator.Content);
        Assert.Equal(ViewerCapabilities.None, coordinator.Capabilities);
    }

    [Fact]
    public async Task CancellingAndSupersedingLoadsDisposeOwnedResourcesExactlyOnce()
    {
        var pending = new TaskCompletionSource<ViewerContent<Resource>>();
        var late = new Resource();
        var current = new Resource();
        var calls = 0;
        using var coordinator = new ViewerCoordinator<Resource>([new ResourceHandler(() =>
            ++calls == 1 ? pending.Task : Task.FromResult(new ViewerContent<Resource>(current, new(1))))]);
        var old = coordinator.LoadAsync(File("old"), 1, 1);
        await coordinator.LoadAsync(File("new"), 1, 1);
        pending.SetResult(new(late, new(1)));
        Assert.Null(await old);
        Assert.Equal(1, late.Disposals);
        coordinator.Cancel();
        coordinator.Cancel();
        Assert.Equal(1, current.Disposals);
        Assert.Null(coordinator.Content);
    }

    private sealed class Resource : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }

    private sealed class ResourceHandler(Func<Task<ViewerContent<Resource>>> load) : IFileViewerHandler<Resource>
    {
        public bool CanHandle(FileEntry file) => true;
        public ViewerCapabilities Capabilities => ViewerCapabilities.None;
        public Task<ViewerContent<Resource>> LoadAsync(FileEntry file, int width, int height, CancellationToken cancellationToken) => load();
    }

    [Fact]
    public async Task CurrentFailureIsReportedRatherThanTreatedAsCancellation()
    {
        using var coordinator = new ViewerCoordinator<string>([
            new Handler(_ => true, ViewerCapabilities.None,
                (_, _) => Task.FromException<ViewerContent<string>>(new IOException("unreadable")))]);
        await Assert.ThrowsAsync<IOException>(() => coordinator.LoadAsync(File("missing"), 100, 100));
        Assert.Null(coordinator.Content);
    }

    [Theory]
    [InlineData(ViewerCapabilities.Zoom, ViewerInteraction.Navigation, false, false, ViewerInputAction.Navigate)]
    [InlineData(ViewerCapabilities.Zoom, ViewerInteraction.Content, false, false, ViewerInputAction.Zoom)]
    [InlineData(ViewerCapabilities.DocumentScroll, ViewerInteraction.Content, false, false, ViewerInputAction.ScrollDocument)]
    [InlineData(ViewerCapabilities.Playback, ViewerInteraction.Content, true, false, ViewerInputAction.None)]
    [InlineData(ViewerCapabilities.Zoom, ViewerInteraction.Content, false, true, ViewerInputAction.Navigate)]
    public void RoutesWheelAccordingToCapabilitiesAndTarget(ViewerCapabilities capabilities, ViewerInteraction interaction,
        bool control, bool carousel, ViewerInputAction expected) =>
        Assert.Equal(expected, ViewerInputPolicy.Wheel(capabilities, interaction, control, carousel));

    [Fact]
    public void ArrowNavigationRespectsControlsAndDocumentInteraction()
    {
        Assert.False(ViewerInputPolicy.CanNavigateWithArrow(true, ViewerCapabilities.Zoom, ViewerInteraction.Navigation));
        Assert.False(ViewerInputPolicy.CanNavigateWithArrow(false, ViewerCapabilities.DocumentScroll, ViewerInteraction.Content));
        Assert.True(ViewerInputPolicy.CanNavigateWithArrow(false, ViewerCapabilities.Zoom, ViewerInteraction.Content));
    }

    private sealed class Handler(Func<FileEntry, bool> supports, ViewerCapabilities capabilities,
        Func<FileEntry, CancellationToken, Task<ViewerContent<string>>> load) : IFileViewerHandler<string>
    {
        public bool CanHandle(FileEntry file) => supports(file);
        public ViewerCapabilities Capabilities => capabilities;
        public Task<ViewerContent<string>> LoadAsync(FileEntry file, int width, int height, CancellationToken cancellationToken) =>
            load(file, cancellationToken);
    }
}
