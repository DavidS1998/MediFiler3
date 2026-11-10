using MediFiler3.Application.Persistence;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class WindowLayoutTests
{
    private static readonly MonitorWorkArea Primary = new("primary", 0, 40, 1920, 1040, 1, true);
    private static WindowLayout Saved => new(50, 100, 1000, 700, false, 300, "primary", 50, 60);

    [Fact]
    public void ExistingMonitorPreservesPositionSizeAndMaximizedState()
    {
        var layout = Saved with { Maximized = true };
        var placed = WindowPlacementPolicy.Resolve(layout, [Primary]);
        Assert.Equal(layout, placed.ToLayout());
        Assert.Equal(50, placed.PixelLeft);
        Assert.Equal(100, placed.PixelTop);
    }

    [Fact]
    public void RearrangedMonitorAndChangedDpiRetainLogicalSizeAndOffsets()
    {
        var monitor = new MonitorWorkArea("primary", -2560, -100, 2560, 1800, 2, true);
        var placed = WindowPlacementPolicy.Resolve(Saved, [monitor]);
        Assert.Equal(1000, placed.Width);
        Assert.Equal(700, placed.Height);
        Assert.Equal(-2460, placed.PixelLeft);
        Assert.Equal(20, placed.PixelTop);
    }

    [Fact]
    public void DisconnectedMonitorFallsBackToPrimaryAndCentersWithinItsWorkArea()
    {
        var placed = WindowPlacementPolicy.Resolve(Saved with { MonitorDeviceName = "removed", Maximized = true }, [Primary]);
        Assert.Equal(460, placed.PixelLeft);
        Assert.Equal(210, placed.PixelTop);
        Assert.True(placed.Maximized);
    }

    [Fact]
    public void LegacyNegativeCoordinatesRemainOnTheLeftMonitor()
    {
        var left = new MonitorWorkArea("left", -1920, 0, 1920, 1080, 1);
        var layout = new WindowLayout(-1800, 50, 1100, 750, false, 280);
        var placed = WindowPlacementPolicy.Resolve(layout, [Primary, left]);
        Assert.Equal("left", placed.Monitor.DeviceName);
        Assert.Equal(-1800, placed.PixelLeft);
        Assert.Equal(50, placed.PixelTop);
        Assert.Equal(280, placed.FolderTreeWidth);
    }

    [Theory]
    [InlineData(-500, -500, 0, 0)]
    [InlineData(5000, 5000, 920, 340)]
    public void PartiallyOrEntirelyOffscreenOffsetsAreClamped(double x, double y, double expectedX, double expectedY)
    {
        var placed = WindowPlacementPolicy.Resolve(Saved with { MonitorOffsetX = x, MonitorOffsetY = y }, [Primary]);
        Assert.Equal(expectedX, placed.OffsetX);
        Assert.Equal(expectedY, placed.OffsetY);
    }

    [Fact]
    public void MissingMonitorMetadataWithNoOverlapUsesPrimary()
    {
        var placed = WindowPlacementPolicy.Resolve(new(50000, -50000, 1000, 700, false, 300), [Primary]);
        Assert.Equal(460, placed.PixelLeft);
        Assert.Equal(210, placed.PixelTop);
    }

    [Fact]
    public void ResolutionSmallerThanMinimumStillKeepsTheWholeWindowVisible()
    {
        var tiny = new MonitorWorkArea("primary", 40, 60, 1200, 750, 1.5, true);
        var placed = WindowPlacementPolicy.Resolve(Saved, [tiny]);
        Assert.Equal(800, placed.Width);
        Assert.Equal(500, placed.Height);
        Assert.Equal(40, placed.PixelLeft);
        Assert.Equal(60, placed.PixelTop);
    }

    [Fact]
    public void VeryLargeSizesAreReducedAndTaskbarSpaceRemainsUncovered()
    {
        var placed = WindowPlacementPolicy.Resolve(Saved with { Width = double.MaxValue, Height = double.MaxValue }, [Primary]);
        Assert.Equal(Primary.LogicalWidth, placed.Width);
        Assert.Equal(Primary.LogicalHeight, placed.Height);
        Assert.Equal(40, placed.PixelTop);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(-1)]
    public void InvalidStoredSizesUseSafeDefaults(double width)
    {
        var placed = WindowPlacementPolicy.Resolve(Saved with { Width = width, Maximized = true }, [Primary]);
        Assert.Equal(1280, placed.Width);
        Assert.Equal(800, placed.Height);
        Assert.False(placed.Maximized);
    }

    [Fact]
    public async Task MonitorMetadataAndLegacyRecordsRoundTripWithoutASchemaChange()
    {
        using var temp = new TemporaryDatabase();
        var service = new WindowLayoutService(new SqliteApplicationStateStore(temp.Database));
        await service.InitializeAsync();
        Assert.Null(service.Current);
        service.Update(Saved with { Maximized = true });
        await service.FlushAsync();
        var reopened = new WindowLayoutService(new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath)));
        await reopened.InitializeAsync();
        Assert.Equal(service.Current, reopened.Current);
    }

    [Fact]
    public async Task RapidChangesCoalesceAndClosingFlushSavesTheLatestCapturedValue()
    {
        var store = new Store();
        var service = new WindowLayoutService(store);
        for (var i = 0; i < 100; i++) service.Update(Saved with { Width = 1000 + i });
        await service.FlushAsync();
        Assert.Equal(1099, store.Value!.Width);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task OlderWriteCannotWinOverNewerCapturedState()
    {
        var store = new Store { Block = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new WindowLayoutService(store);
        service.Update(Saved);
        var first = service.FlushAsync();
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Update(Saved with { Maximized = true, Width = 1200 });
        store.Block.SetResult();
        await first;
        await service.FlushAsync();
        Assert.Equal(1200, store.Value!.Width);
        Assert.True(store.Value.Maximized);
    }

    [Fact]
    public async Task InvalidOrUnreadableStateIsReportedAndNotOverwrittenWithoutAChange()
    {
        var store = new Store { Value = Saved with { MonitorOffsetX = double.NaN } };
        var service = new WindowLayoutService(store);
        Exception? failure = null;
        service.PersistenceFailed += exception => failure = exception;
        await service.InitializeAsync();
        Assert.Null(service.Current);
        Assert.NotNull(failure);
        Assert.True(service.LoadFailed);
        await service.FlushAsync();
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task FailedSaveRemainsDirtyAndCanBeRetried()
    {
        var store = new Store { Fail = true };
        var service = new WindowLayoutService(store);
        service.Update(Saved);
        await Assert.ThrowsAsync<IOException>(() => service.FlushAsync());
        Assert.Equal(Saved, service.Current);
        store.Fail = false;
        await service.FlushAsync();
        Assert.Equal(Saved, store.Value);
    }

    private sealed class Store : IWindowLayoutStore
    {
        public WindowLayout? Value;
        public int Writes;
        public bool Fail;
        public TaskCompletionSource? Block;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<WindowLayout?> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public async Task SaveAsync(WindowLayout layout, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (Block is not null) await Block.Task;
            if (Fail) throw new IOException("Simulated save failure.");
            Value = layout;
            Writes++;
        }
    }
}
