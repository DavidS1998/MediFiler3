using MediFiler3.Application.Files;
using MediFiler3.Infrastructure.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class DefaultApplicationTests
{
    [Fact]
    public async Task NativeLookupDoesNotMistakeWindowsChooserForDefaultApplication()
    {
        var result = await new WindowsDefaultApplicationService(new WindowsDefaultApplicationLookup())
            .LookupAsync("file.medifiler-unassociated-" + Guid.NewGuid().ToString("N"), CancellationToken.None);
        Assert.Equal(DefaultApplicationStatus.None, result.Status);
        Assert.Null(result.DisplayName);
        Assert.Null(result.IconPng);
    }

    [Fact]
    public async Task LookupWorkIsBoundedAndQueuedRequestsCanBeCancelled()
    {
        using var lookup = new BlockingLookup();
        var service = new WindowsDefaultApplicationService(lookup);
        var first = service.LookupAsync("first.pdf", CancellationToken.None);
        try
        {
            await lookup.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var cancellation = new CancellationTokenSource();
            var second = service.LookupAsync("second.pdf", cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            Assert.Equal(1, lookup.Calls);
        }
        finally { lookup.Release.Set(); }
        await first;
    }

    private sealed class BlockingLookup : IDefaultApplicationLookup, IDisposable
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new();
        public int Calls { get; private set; }
        public DefaultApplicationInfo Query(string extension)
        {
            Calls++;
            Started.TrySetResult();
            Release.Wait();
            return new(DefaultApplicationStatus.Available, "Example");
        }
        public void Dispose() => Release.Dispose();
    }

    [Fact]
    public async Task NativeLookupRunsOnStaAndPreservesNameAndIcon()
    {
        var expected = new DefaultApplicationInfo(DefaultApplicationStatus.Available, "Example Viewer", [1, 2, 3]);
        var adapter = new Lookup(expected);
        var result = await new WindowsDefaultApplicationService(adapter).LookupAsync("C:\\folder\\document.PDF", CancellationToken.None);
        Assert.Same(expected, result);
        Assert.Equal(".PDF", adapter.Extension);
        Assert.Equal(ApartmentState.STA, adapter.Apartment);
    }

    [Fact]
    public async Task UnavailableAndUnknownAssociationsAreDistinct()
    {
        var none = await new WindowsDefaultApplicationService(new Lookup(new(DefaultApplicationStatus.None)))
            .LookupAsync("file.unknown", CancellationToken.None);
        var unknown = await new WindowsDefaultApplicationService(new Lookup(new(DefaultApplicationStatus.Available)) { Fail = true })
            .LookupAsync("file.pdf", CancellationToken.None);
        Assert.Equal(DefaultApplicationStatus.None, none.Status);
        Assert.Equal(DefaultApplicationStatus.Unknown, unknown.Status);
        var adapter = new Lookup(new(DefaultApplicationStatus.Available));
        Assert.Equal(DefaultApplicationStatus.Unknown, (await new WindowsDefaultApplicationService(adapter)
            .LookupAsync("README", CancellationToken.None)).Status);
        Assert.Null(adapter.Extension);
    }

    [Fact]
    public async Task CancelledLookupDoesNotReachNativeAdapter()
    {
        var adapter = new Lookup(new(DefaultApplicationStatus.Available));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsDefaultApplicationService(adapter).LookupAsync("file.pdf", cancellation.Token));
        Assert.Null(adapter.Extension);
    }

    [Fact]
    public async Task LateLookupCannotReplaceNewFileOrRefreshedAssociation()
    {
        var service = new DeferredService();
        using var resolver = new DefaultApplicationResolver(service);
        var first = resolver.ResolveAsync("first.pdf");
        var second = resolver.ResolveAsync("second.pdf");
        Assert.True(service.Requests[0].Token.IsCancellationRequested);
        service.Requests[1].Completion.SetResult(new(DefaultApplicationStatus.Available, "New App", [4]));
        await second;
        service.Requests[0].Completion.SetResult(new(DefaultApplicationStatus.Available, "Old App", [1]));
        await first;
        Assert.Equal("New App", resolver.Application!.DisplayName);
        Assert.Equal(new byte[] { 4 }, resolver.Application.IconPng);

        var refresh = resolver.ResolveAsync("second.pdf");
        Assert.Null(resolver.Application);
        service.Requests[2].Completion.SetResult(new(DefaultApplicationStatus.Available, "Changed App"));
        await refresh;
        Assert.Equal("Changed App", resolver.Application!.DisplayName);
        resolver.Clear();
        Assert.Null(resolver.Application);
    }

    [Fact]
    public async Task ClearingPendingLookupRejectsLateFailure()
    {
        var service = new DeferredService();
        using var resolver = new DefaultApplicationResolver(service);
        var pending = resolver.ResolveAsync("file.pdf");
        resolver.Clear();
        service.Requests[0].Completion.SetException(new IOException("obsolete failure"));
        await pending;
        Assert.Null(resolver.Application);
    }

    [Fact]
    public void ButtonStateDistinguishesLoadingKnownMissingAndBusy()
    {
        var loading = new ExternalOpenState(true, false, null);
        Assert.Equal("Open in default application", loading.DefaultLabel);
        Assert.True(loading.CanOpenDefault);
        var known = loading with { Application = new(DefaultApplicationStatus.Available, "Example App") };
        Assert.Equal("Open in Example App", known.DefaultLabel);
        Assert.False((known with { Busy = true }).CanOpenDefault);
        Assert.False((known with { Busy = true }).CanChooseApplication);
        var missing = loading with { Application = new(DefaultApplicationStatus.None) };
        Assert.Equal("No default application", missing.DefaultLabel);
        Assert.False(missing.CanOpenDefault);
        Assert.True(missing.CanChooseApplication);
        Assert.False((loading with { Visible = false }).CanOpenDefault);
        Assert.False((loading with { Visible = false }).CanChooseApplication);
    }

    private sealed class Lookup(DefaultApplicationInfo info) : IDefaultApplicationLookup
    {
        public bool Fail { get; init; }
        public string? Extension { get; private set; }
        public ApartmentState Apartment { get; private set; }
        public DefaultApplicationInfo Query(string extension)
        {
            Extension = extension;
            Apartment = Thread.CurrentThread.GetApartmentState();
            if (Fail) throw new IOException("lookup unavailable");
            return info;
        }
    }

    private sealed class DeferredService : IDefaultApplicationService
    {
        public List<(CancellationToken Token, TaskCompletionSource<DefaultApplicationInfo> Completion)> Requests { get; } = [];
        public Task<DefaultApplicationInfo> LookupAsync(string path, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<DefaultApplicationInfo>();
            Requests.Add((cancellationToken, completion));
            return completion.Task;
        }
    }
}
