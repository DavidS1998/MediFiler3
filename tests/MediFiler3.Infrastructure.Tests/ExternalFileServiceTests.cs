using MediFiler3.Application.Files;
using MediFiler3.Infrastructure.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class ExternalFileServiceTests
{
    [Theory]
    [InlineData(0, ExternalFileOutcome.Opened)]
    [InlineData(1155, ExternalFileOutcome.NoAssociation)]
    [InlineData(31, ExternalFileOutcome.NoAssociation)]
    [InlineData(1223, ExternalFileOutcome.Cancelled)]
    [InlineData(2, ExternalFileOutcome.MissingFile)]
    [InlineData(3, ExternalFileOutcome.MissingFile)]
    [InlineData(5, ExternalFileOutcome.Failed)]
    public async Task DefaultOpeningPreservesPathOwnerAndReportsShellOutcome(int error, ExternalFileOutcome expected)
    {
        using var file = new TestFile();
        var adapter = new Launcher { Result = error };
        var result = await new WindowsExternalFileService(adapter).OpenAsync(file.Path, ExternalFileAction.DefaultApplication, 123);
        Assert.Equal(expected, result.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
        Assert.Equal(file.Path, adapter.Path);
        Assert.Equal((nint)123, adapter.Owner);
        Assert.Equal(ApartmentState.STA, adapter.Apartment);
        Assert.Equal(1, adapter.DefaultCalls);
        Assert.Equal(0, adapter.ChooserCalls); // No automatic second launch on association failure.
        Assert.Equal("original content", await File.ReadAllTextAsync(file.Path));
    }

    [Theory]
    [InlineData(0, ExternalFileOutcome.Opened)]
    [InlineData(1, ExternalFileOutcome.Cancelled)]
    [InlineData(unchecked((int)0x800704c7), ExternalFileOutcome.Cancelled)]
    [InlineData(unchecked((int)0x80004004), ExternalFileOutcome.Cancelled)]
    [InlineData(unchecked((int)0x80070002), ExternalFileOutcome.MissingFile)]
    [InlineData(unchecked((int)0x80004005), ExternalFileOutcome.Failed)]
    public async Task ChooserDistinguishesCancellationAndFailure(int error, ExternalFileOutcome expected)
    {
        using var file = new TestFile();
        var adapter = new Launcher { Result = error };
        var result = await new WindowsExternalFileService(adapter).OpenAsync(file.Path, ExternalFileAction.ChooseApplication, 0);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(1, adapter.ChooserCalls);
        Assert.Equal(0, adapter.DefaultCalls);
        Assert.Equal(ApartmentState.STA, adapter.Apartment);
    }

    [Fact]
    public async Task MissingRelativeAndDirectoryPathsNeverReachLauncher()
    {
        var adapter = new Launcher();
        var service = new WindowsExternalFileService(adapter);
        var missing = await service.OpenAsync(System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N")),
            ExternalFileAction.DefaultApplication, 0);
        Assert.Equal(ExternalFileOutcome.MissingFile, missing.Outcome);
        Assert.Equal(ExternalFileOutcome.Failed, (await service.OpenAsync("relative.txt", ExternalFileAction.ChooseApplication, 0)).Outcome);
        Assert.Equal(ExternalFileOutcome.Failed, (await service.OpenAsync(System.IO.Path.GetTempPath(), ExternalFileAction.ChooseApplication, 0)).Outcome);
        Assert.Equal(0, adapter.DefaultCalls + adapter.ChooserCalls);
    }

    [Fact]
    public async Task CancelledRequestDoesNotOpenAnything()
    {
        var adapter = new Launcher();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new WindowsExternalFileService(adapter)
            .OpenAsync("not-used", ExternalFileAction.DefaultApplication, 0, cancellation.Token));
        Assert.Equal(0, adapter.DefaultCalls + adapter.ChooserCalls);
    }

    [Fact]
    public async Task AdapterAccessFailureReturnsReadableError()
    {
        using var file = new TestFile();
        var result = await new WindowsExternalFileService(new Launcher { Failure = new UnauthorizedAccessException("Access denied") })
            .OpenAsync(file.Path, ExternalFileAction.DefaultApplication, 0);
        Assert.Equal(ExternalFileOutcome.Failed, result.Outcome);
        Assert.Contains("Access denied", result.Message);
    }

    private sealed class Launcher : IWindowsFileLaunchAdapter
    {
        public int Result { get; init; }
        public Exception? Failure { get; init; }
        public int DefaultCalls { get; private set; }
        public int ChooserCalls { get; private set; }
        public string? Path { get; private set; }
        public nint Owner { get; private set; }
        public ApartmentState Apartment { get; private set; }
        public int OpenDefault(string fullPath, nint owner) { DefaultCalls++; return Capture(fullPath, owner); }
        public int OpenWith(string fullPath, nint owner) { ChooserCalls++; return Capture(fullPath, owner); }
        private int Capture(string path, nint owner)
        {
            Path = path; Owner = owner; Apartment = Thread.CurrentThread.GetApartmentState();
            if (Failure is not null) throw Failure;
            return Result;
        }
    }

    private sealed class TestFile : IDisposable
    {
        private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MediFiler3External", Guid.NewGuid().ToString("N"));
        public string Path { get; }
        public TestFile()
        {
            Directory.CreateDirectory(_folder);
            Path = System.IO.Path.Combine(_folder, "sample [1] 'quoted' $name 空.unknown");
            File.WriteAllText(Path, "original content");
        }
        public void Dispose() { File.Delete(Path); Directory.Delete(_folder); }
    }
}
