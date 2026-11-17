using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class NavigationControllerTests
{
    private static FileEntry File(string name) => new(name, name, 1, DateTime.UtcNow);

    [Fact]
    public async Task FilteredOverscrollOnlyActivatesMatchingFolders()
    {
        var skipped = new FolderTreeEntry("Skipped", "root/skipped");
        var target = new FolderTreeEntry("Target", "root/target");
        var root = new FolderTreeEntry("Root", "root", [skipped, target]);
        var state = new NavigationSessionState();
        state.ReplaceRoots([root]);
        state.SetActiveFolder(root);
        state.ReplaceFiles([File("only")]);
        var activated = new TaskCompletionSource<FolderTreeEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new NavigationController(state, folder =>
        {
            state.SetActiveFolder(folder);
            state.ReplaceFiles([File("next")]);
            activated.TrySetResult(folder);
            return Task.CompletedTask;
        })
        {
            ProbeFolder = async (folder, token) =>
            {
                await Task.Yield();
                token.ThrowIfCancellationRequested();
                return ReferenceEquals(folder, target);
            }
        };

        for (var attempt = 0; attempt < 4; attempt++) controller.NavigateByOneFile(1);
        Assert.Same(root, state.ActiveFolder);
        Assert.False(activated.Task.IsCompleted);
        controller.NavigateByOneFile(1);
        Assert.Same(target, await activated.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void DisabledOverscrollKeepsNormalNavigationAndResetsAccumulatedBoundaryAttempts()
    {
        var root = new FolderTreeEntry("Root", "root");
        var state = new NavigationSessionState();
        state.ReplaceRoots([root]); state.SetActiveFolder(root);
        var first = File("first"); var last = File("last"); state.ReplaceFiles([first, last]);
        var activations = 0;
        var controller = new NavigationController(state, _ => { activations++; return Task.CompletedTask; });
        controller.NavigateByOneFile(1);
        for (var i = 0; i < 4; i++) controller.NavigateByOneFile(1);
        controller.SetOverscrollEnabled(false);
        for (var i = 0; i < 20; i++) controller.NavigateByOneFile(1);
        Assert.Equal(0, activations); Assert.Same(last, state.CurrentFile);
        controller.NavigateByOneFile(-1); Assert.Same(first, state.CurrentFile);
        for (var i = 0; i < 20; i++) controller.NavigateByOneFile(-1);
        Assert.Equal(0, activations);
        controller.SetOverscrollEnabled(true);
        controller.NavigateByOneFile(1);
        for (var i = 0; i < 4; i++) controller.NavigateByOneFile(1);
        Assert.Equal(0, activations);
    }

    [Fact]
    public void NavigationUpdatesSharedSelectionAndRejectsFilesOutsideCurrentFolder()
    {
        var state = new NavigationSessionState();
        var first = File("first"); var second = File("second");
        state.ReplaceFiles([first, second]);
        var controller = new NavigationController(state, _ => Task.CompletedTask);
        var selected = new List<FileEntry>();
        controller.FileSelected += selected.Add;
        controller.NavigateByOneFile(1);
        Assert.Same(second, state.CurrentFile);
        Assert.Equal(2, state.CurrentFilePosition);
        controller.SelectFile(File("foreign"));
        Assert.Single(selected);
        Assert.Same(second, state.CurrentFile);
    }

    [Fact]
    public void FifthConsecutiveBoundaryAttemptSkipsEmptyFolderInPreorder()
    {
        var empty = new FolderTreeEntry("Empty", "root/empty");
        var target = new FolderTreeEntry("Target", "root/target");
        var root = new FolderTreeEntry("Root", "root", [empty, target]);
        var state = new NavigationSessionState();
        state.ReplaceRoots([root]); state.SetActiveFolder(root); state.ReplaceFiles([File("first")]);
        var activated = new List<FolderTreeEntry>();
        var controller = new NavigationController(state, folder =>
        {
            activated.Add(folder);
            state.SetActiveFolder(folder);
            state.ReplaceFiles(ReferenceEquals(folder, target) ? [File("next")] : []);
            return Task.CompletedTask;
        });
        for (var index = 0; index < 4; index++) controller.NavigateByOneFile(1);
        Assert.Empty(activated);
        controller.NavigateByOneFile(1);
        Assert.Equal(new[] { empty, target }, activated);
        Assert.Same(target, state.ActiveFolder);
    }

    [Fact]
    public async Task BackwardOverscrollSkipsEmptyFoldersAndSelectsTheirPredecessorsLastFile()
    {
        var previous = new FolderTreeEntry("Previous", "root/previous");
        var empty = new FolderTreeEntry("Empty", "root/empty");
        var current = new FolderTreeEntry("Current", "root/current");
        var state = new NavigationSessionState();
        state.ReplaceRoots([previous, empty, current]);
        state.SetActiveFolder(current);
        state.ReplaceFiles([File("current-first"), File("current-last")]);
        var previousFiles = new[] { File("previous-first"), File("previous-middle"), File("previous-last") };
        var activated = new List<FolderTreeEntry>();
        var presented = new TaskCompletionSource<FileEntry>(TaskCreationOptions.RunContinuationsAsynchronously);
        var controller = new NavigationController(state, async folder =>
        {
            activated.Add(folder);
            await Task.Yield(); // Exercise the actual asynchronous folder-listing boundary.
            state.SetActiveFolder(folder);
            state.ReplaceFiles(ReferenceEquals(folder, previous) ? previousFiles : []);
        });
        controller.FileSelected += file => presented.TrySetResult(file);

        for (int attempt = 0; attempt < 4; attempt++) controller.NavigateByOneFile(-1);
        Assert.Empty(activated);
        controller.NavigateByOneFile(-1);

        Assert.Same(previousFiles[^1], await presented.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(new[] { empty, previous }, activated);
        Assert.Same(previous, state.ActiveFolder);
        Assert.Same(previousFiles[^1], state.CurrentFile);
        Assert.Equal(3, state.CurrentFilePosition);
        controller.NavigateByOneFile(-1);
        Assert.Same(previousFiles[1], state.CurrentFile);
    }

    [Fact]
    public void ForwardOverscrollStillEntersTheNextFolderAtItsFirstFile()
    {
        var current = new FolderTreeEntry("Current", "current");
        var next = new FolderTreeEntry("Next", "next");
        var state = new NavigationSessionState();
        state.ReplaceRoots([current, next]);
        state.SetActiveFolder(current);
        state.ReplaceFiles([File("current")]);
        var nextFiles = new[] { File("next-first"), File("next-last") };
        var controller = new NavigationController(state, folder =>
        {
            state.SetActiveFolder(folder);
            state.ReplaceFiles(nextFiles);
            return Task.CompletedTask;
        });

        for (int attempt = 0; attempt < 5; attempt++) controller.NavigateByOneFile(1);

        Assert.Same(next, state.ActiveFolder);
        Assert.Same(nextFiles[0], state.CurrentFile);
        Assert.Equal(1, state.CurrentFilePosition);
    }

    [Fact]
    public void ReversingDirectionResetsBoundaryAttempts()
    {
        var root = new FolderTreeEntry("Root", "root");
        var state = new NavigationSessionState();
        state.ReplaceRoots([root]); state.SetActiveFolder(root); state.ReplaceFiles([File("only")]);
        var activations = 0;
        var controller = new NavigationController(state, _ => { activations++; return Task.CompletedTask; });
        for (var index = 0; index < 4; index++) controller.NavigateByOneFile(1);
        controller.NavigateByOneFile(-1);
        controller.NavigateByOneFile(1);
        Assert.Equal(0, activations);
    }
}
