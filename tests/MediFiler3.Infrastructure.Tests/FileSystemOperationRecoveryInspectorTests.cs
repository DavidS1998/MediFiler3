using System.Text.Json;
using MediFiler3.Application.Operations;
using MediFiler3.Domain.Operations;
using MediFiler3.Infrastructure.Persistence;

namespace MediFiler3.Infrastructure.Tests;

public sealed class FileSystemOperationRecoveryInspectorTests
{
    [Fact]
    public void WindowsIdentityProviderReturnsStableIdentityAndDistinguishesMissingPaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), "MediFiler3Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "identity.txt");
        try
        {
            File.WriteAllText(path, "identity test");
            var provider = new WindowsFileIdentityProvider();

            var first = provider.GetIdentity(path);
            var second = provider.GetIdentity(path);

            Assert.True(first.Exists);
            Assert.True(first.Accessible);
            Assert.False(string.IsNullOrWhiteSpace(first.Identity));
            Assert.Equal(first.Identity, second.Identity);

            File.Delete(path);
            var missing = provider.GetIdentity(path);
            Assert.False(missing.Exists);
            Assert.True(missing.Accessible);
            Assert.Null(missing.Identity);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SameVolumeMoveIsCompletedOnlyWhenIdentityMovedToDestination()
    {
        var identities = new FakeFileIdentityProvider()
            .Set("source", new FileIdentityLookup(false, true, null))
            .Set("destination", Found("file-1"));
        var inspector = new FileSystemOperationRecoveryInspector(identities);

        var result = await inspector.AssessAsync(CreateOperation(
            FileOperationKind.Move, "source", "destination",
            JsonSerializer.Serialize(new PathOperationPayload(false, false, "file-1"))));

        Assert.Equal(RecoveryConclusion.Completed, result.Conclusion);
    }

    [Fact]
    public async Task SameVolumeMoveIsFailedOnlyWhenOriginalIdentityRemainsAtSource()
    {
        var identities = new FakeFileIdentityProvider()
            .Set("source", Found("file-1"))
            .Set("destination", new FileIdentityLookup(false, true, null));
        var inspector = new FileSystemOperationRecoveryInspector(identities);

        var result = await inspector.AssessAsync(CreateOperation(
            FileOperationKind.Rename, "source", "destination",
            JsonSerializer.Serialize(new PathOperationPayload(false, false, "file-1"))));

        Assert.Equal(RecoveryConclusion.Failed, result.Conclusion);
    }

    [Theory]
    [InlineData("replacement-id", true, "Replacement identity at destination")]
    [InlineData(null, false, "Inaccessible source")]
    public async Task MoveWithUnprovenPathsRemainsAmbiguous(string? destinationId, bool sourceAccessible, string _)
    {
        var identities = new FakeFileIdentityProvider()
            .Set("source", new FileIdentityLookup(true, sourceAccessible, sourceAccessible ? "different-id" : null))
            .Set("destination", destinationId is null
                ? new FileIdentityLookup(true, false, null)
                : Found(destinationId));
        var inspector = new FileSystemOperationRecoveryInspector(identities);

        var result = await inspector.AssessAsync(CreateOperation(
            FileOperationKind.Move, "source", "destination",
            JsonSerializer.Serialize(new PathOperationPayload(false, false, "file-1"))));

        Assert.Equal(RecoveryConclusion.Ambiguous, result.Conclusion);
    }

    [Fact]
    public async Task CrossVolumeMoveRequiresVerifiedDestinationAndMissingSource()
    {
        var details = JsonSerializer.Serialize(new OperationEventDetails(VerifiedDestinationIdentity: "copy-1"));
        var entry = CreateOperation(FileOperationKind.Move, "source", "destination",
            JsonSerializer.Serialize(new PathOperationPayload(false, true, "source-1")),
            (FileOperationEventKind.Prepared, null),
            (FileOperationEventKind.DestinationVerified, details));
        var identities = new FakeFileIdentityProvider()
            .Set("source", new FileIdentityLookup(false, true, null))
            .Set("destination", Found("copy-1"));

        var result = await new FileSystemOperationRecoveryInspector(identities).AssessAsync(entry);

        Assert.Equal(RecoveryConclusion.Completed, result.Conclusion);
    }

    [Fact]
    public async Task CrossVolumeMoveWithBothPathsPresentRemainsAmbiguous()
    {
        var details = JsonSerializer.Serialize(new OperationEventDetails(VerifiedDestinationIdentity: "copy-1"));
        var entry = CreateOperation(FileOperationKind.Move, "source", "destination",
            JsonSerializer.Serialize(new PathOperationPayload(false, true, "source-1")),
            (FileOperationEventKind.Prepared, null),
            (FileOperationEventKind.DestinationVerified, details));
        var identities = new FakeFileIdentityProvider()
            .Set("source", Found("source-1"))
            .Set("destination", Found("copy-1"));

        var result = await new FileSystemOperationRecoveryInspector(identities).AssessAsync(entry);

        Assert.Equal(RecoveryConclusion.Ambiguous, result.Conclusion);
    }

    [Fact]
    public async Task CreatedFolderCompletesOnlyWithRecordedFolderIdentity()
    {
        var payload = JsonSerializer.Serialize(new CreateFolderPayload(CreatedFolderIdentity: "folder-id"));
        var inspector = new FileSystemOperationRecoveryInspector(new FakeFileIdentityProvider()
            .Set("folder", Found("folder-id")));

        var result = await inspector.AssessAsync(CreateOperation(FileOperationKind.CreateFolder, null, "folder", payload));

        Assert.Equal(RecoveryConclusion.Completed, result.Conclusion);
    }

    [Fact]
    public async Task RecycleBinOperationWithoutRestoreEvidenceRemainsAmbiguous()
    {
        var inspector = new FileSystemOperationRecoveryInspector(new FakeFileIdentityProvider()
            .Set("source", new FileIdentityLookup(false, true, null)));

        var result = await inspector.AssessAsync(CreateOperation(
            FileOperationKind.SendToRecycleBin, "source", null,
            JsonSerializer.Serialize(new RecycleBinPayload())));

        Assert.Equal(RecoveryConclusion.Ambiguous, result.Conclusion);
    }

    private static FileIdentityLookup Found(string id) => new(true, true, id);

    private static JournaledFileOperation CreateOperation(
        FileOperationKind kind,
        string? source,
        string? destination,
        string? payload,
        params (FileOperationEventKind Kind, string? Details)[] events)
    {
        var id = Guid.NewGuid();
        var record = new FileOperationRecord(id, null, kind, source, destination, DateTimeOffset.UtcNow, 1, payload);
        var journalEvents = events.Length == 0
            ? [new FileOperationEvent(1, id, FileOperationEventKind.Prepared, DateTimeOffset.UtcNow, null)]
            : events.Select((entry, index) => new FileOperationEvent(
                index + 1, id, entry.Kind, DateTimeOffset.UtcNow, entry.Details)).ToArray();
        return new JournaledFileOperation(record, journalEvents);
    }

    private sealed class FakeFileIdentityProvider : IFileIdentityProvider
    {
        private readonly Dictionary<string, FileIdentityLookup> _lookups = new(StringComparer.OrdinalIgnoreCase);

        public FakeFileIdentityProvider Set(string path, FileIdentityLookup result)
        {
            _lookups[path] = result;
            return this;
        }

        public FileIdentityLookup GetIdentity(string path) => _lookups.TryGetValue(path, out var identity)
            ? identity
            : new FileIdentityLookup(false, true, null);
    }
}
