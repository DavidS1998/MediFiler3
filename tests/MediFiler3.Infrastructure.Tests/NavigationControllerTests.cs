using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class NavigationControllerTests
{
    private static FileEntry File(string name) => new(name, name, 1, DateTime.UtcNow);

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
