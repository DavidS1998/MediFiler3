using MediFiler3.Application.Persistence;
using MediFiler3.Application.Viewing;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ApplicationSettingsServiceTests
{
    [Fact]
    public async Task RapidChangesCoalesceAndShutdownFlushPersistsLatestSnapshot()
    {
        var store = new Store();
        var service = new ApplicationSettingsService(store);
        await service.InitializeAsync();
        for (var i = 0; i < 100; i++) service.Update(s => s with { Volume = i / 100d });
        service.Update(s => s with { Muted = true, CarouselIconCount = 25, FolderTreePinned = true });
        await service.FlushAsync();
        Assert.Equal(new ApplicationSettings(true, 0.99, 25, true), store.Value);
        Assert.Equal(1, store.Writes);
        await service.FlushAsync();
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task NewChangesDuringSaveCannotBeOverwrittenByOlderSave()
    {
        var store = new Store { Block = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        var service = new ApplicationSettingsService(store);
        service.Update(s => s with { Volume = 0.2 });
        var first = service.FlushAsync();
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Update(s => s with { Volume = 0.8, Muted = true });
        store.Block.SetResult();
        await first;
        await service.FlushAsync();
        Assert.Equal(0.8, store.Value.Volume);
        Assert.True(store.Value.Muted);
    }

    [Fact]
    public async Task RestartLoadsAllSettingsBeforeConstructingMediaAudioState()
    {
        using var temp = new TemporaryDatabase();
        var first = new ApplicationSettingsService(new SqliteApplicationStateStore(temp.Database));
        await first.InitializeAsync();
        first.Update(s => new ApplicationSettings(true, 0.73, 19, true));
        await first.FlushAsync();
        var second = new ApplicationSettingsService(new SqliteApplicationStateStore(new SqliteDatabase(temp.DatabasePath)));
        await second.InitializeAsync();
        Assert.Equal(first.Current, second.Current);
        var audio = new MediaAudioSettings(second.Current.Volume, second.Current.Muted);
        Assert.Equal(0.73, audio.Volume);
        Assert.True(audio.Muted);
    }

    [Fact]
    public async Task InvalidStoredValuesUseDefaultsWithoutOverwritingStoredData()
    {
        var store = new Store { Value = new ApplicationSettings(Volume: double.NaN) };
        var service = new ApplicationSettingsService(store);
        Exception? error = null;
        service.PersistenceFailed += exception => error = exception;
        await service.InitializeAsync();
        Assert.NotNull(error);
        Assert.Equal(new ApplicationSettings(), service.Current);
        await service.FlushAsync();
        Assert.Equal(0, store.Writes);
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Update(s => s with { CarouselIconCount = 4 }));
    }

    [Fact]
    public async Task SaveFailureLeavesDirtyStateForRetry()
    {
        var store = new Store { Fail = true };
        var service = new ApplicationSettingsService(store);
        service.Update(s => s with { Muted = true });
        await Assert.ThrowsAsync<IOException>(() => service.FlushAsync());
        Assert.True(service.Current.Muted);
        store.Fail = false;
        await service.FlushAsync();
        Assert.True(store.Value.Muted);
    }

    private sealed class Store : IApplicationSettingsStore
    {
        public ApplicationSettings Value = new();
        public int Writes;
        public bool Fail;
        public TaskCompletionSource? Block;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ApplicationSettings> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(Value);
        public async Task SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            if (Block is not null) await Block.Task;
            if (Fail) throw new IOException("Simulated settings save failure.");
            Value = settings;
            Writes++;
        }
    }
}
