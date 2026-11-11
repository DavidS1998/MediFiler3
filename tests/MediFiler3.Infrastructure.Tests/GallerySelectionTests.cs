using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class GallerySelectionTests
{
    private static FileEntry File(string name, long size = 1) => new(name, name, size, DateTime.UnixEpoch);
    private static readonly FileEntry[] Files = [File("a"), File("b"), File("c"), File("d")];

    [Fact]
    public void ToggleAndRangeKeepFocusAndAnchorDistinctAndReturnSortedSnapshots()
    {
        var selection = new GallerySelectionState();
        selection.Select(Files, Files[2]);
        selection.Select(Files, Files[0], toggle: true);
        var snapshot = selection.GetSelectedFiles(Files);
        Assert.Equal(new[] { Files[0], Files[2] }, snapshot);
        selection.Focus(Files, Files[3]);
        Assert.Equal("a", selection.AnchorPath);
        Assert.Equal("d", selection.FocusedPath);
        selection.Select(Files, Files[1], range: true);
        Assert.Equal(Files.Take(2), selection.GetSelectedFiles(Files));
        Assert.Equal(2, snapshot.Count);
        selection.Select(Files, Files[1], toggle: true);
        Assert.False(selection.IsSelected(Files[1]));
        Assert.Equal("b", selection.FocusedPath);
    }

    [Fact]
    public void ReverseAndAdditiveRangesUseStableAnchor()
    {
        var selection = new GallerySelectionState();
        selection.Select(Files, Files[3]);
        selection.Select(Files, Files[1], range: true);
        Assert.Equal(Files.Skip(1), selection.GetSelectedFiles(Files));
        Assert.Equal("d", selection.AnchorPath);
        selection.Select(Files, Files[2], range: true);
        Assert.Equal(Files.Skip(2), selection.GetSelectedFiles(Files));
        selection.Select(Files, Files[0], toggle: true, range: true);
        Assert.Equal(Files, selection.GetSelectedFiles(Files));
    }

    [Fact]
    public void ForeignItemsAreIgnoredAndMissingAnchorStartsNewRange()
    {
        var selection = new GallerySelectionState();
        selection.Select(Files, File("foreign"));
        selection.Focus(Files, File("foreign"));
        Assert.Null(selection.FocusedPath);
        selection.Select(Files, Files[2], range: true);
        Assert.Equal("c", selection.AnchorPath);
        Assert.Single(selection.GetSelectedFiles(Files));
    }

    [Fact]
    public void ReconciliationKeepsPathsAcrossMetadataChangesAndPrunesRemovedFocusAndAnchor()
    {
        var selection = new GallerySelectionState();
        selection.SelectAll(Files);
        selection.Select(Files, Files[2], toggle: true);
        selection.Reconcile([File("A", 99), File("d", 10)]);
        Assert.Equal(2, selection.Count);
        Assert.Null(selection.FocusedPath);
        Assert.Null(selection.AnchorPath);
        Assert.Equal(99, selection.GetSelectedFiles([File("A", 99)])[0].Size);
        selection.Clear();
        Assert.Equal(0, selection.Count);
        Assert.Null(selection.FocusedPath);
    }

    [Fact]
    public void ModeSwitchPreservesCollectionAndCurrentContextWithoutFolderActivation()
    {
        var state = new NavigationSessionState();
        var root = new FolderTreeEntry("root", "root");
        state.ReplaceRoots([root]); state.SetActiveFolder(root); state.ReplaceFiles(Files);
        var activations = 0;
        var controller = new NavigationController(state, _ => { activations++; return Task.CompletedTask; });
        controller.SelectFile(Files[2]);
        controller.SwitchViewingMode(ViewingMode.Gallery);
        state.GallerySelection.Select(state.Files, Files[0]);
        controller.SwitchViewingMode(ViewingMode.Gallery);
        Assert.Equal(1, state.GallerySelection.Count);
        controller.SwitchViewingMode(ViewingMode.Browse);
        Assert.Equal(0, state.GallerySelection.Count);
        Assert.Same(root, state.ActiveRoot);
        Assert.Same(root, state.ActiveFolder);
        Assert.Same(Files[2], state.CurrentFile);
        Assert.Equal(Files, state.Files);
        Assert.Equal(0, activations);
    }

    [Fact]
    public void BrowseHandoffValidatesTargetAndPublishesCurrentRecordOnce()
    {
        var state = new NavigationSessionState();
        state.ReplaceFiles(Files);
        var controller = new NavigationController(state, _ => throw new InvalidOperationException());
        controller.SwitchViewingMode(ViewingMode.Gallery);
        state.GallerySelection.Select(state.Files, Files[1]);
        var presented = new List<FileEntry>();
        controller.FileSelected += presented.Add;
        Assert.False(controller.OpenGalleryFileInBrowse(File("missing")));
        Assert.Equal(ViewingMode.Gallery, state.ViewingMode);
        Assert.Equal(1, state.GallerySelection.Count);
        Assert.True(controller.OpenGalleryFileInBrowse(File("B", 100)));
        Assert.Same(Files[1], Assert.Single(presented));
        Assert.Same(Files[1], state.CurrentFile);
        Assert.Equal(2, state.CurrentFilePosition);
        Assert.Equal(ViewingMode.Browse, state.ViewingMode);
        Assert.Equal(0, state.GallerySelection.Count);
        Assert.False(controller.OpenGalleryFileInBrowse(Files[1]));
    }

    [Fact]
    public void FolderAndRootChangesClearSelectionButFileRefreshRetainsSurvivors()
    {
        var state = new NavigationSessionState();
        var root = new FolderTreeEntry("root", "root");
        state.ReplaceRoots([root]); state.SetActiveFolder(root); state.ReplaceFiles(Files);
        state.SwitchViewingMode(ViewingMode.Gallery);
        state.GallerySelection.SelectAll(state.Files);
        state.ReplaceFiles([File("b", 20), File("d", 30)]);
        Assert.Equal(2, state.GallerySelection.Count);
        state.Files.RemoveAt(0);
        Assert.Equal(1, state.GallerySelection.Count);
        state.SetActiveFolder(null);
        Assert.Equal(0, state.GallerySelection.Count);
        state.ReplaceFiles(Files); state.GallerySelection.SelectAll(state.Files);
        state.ReplaceRoots([root]);
        Assert.Equal(0, state.GallerySelection.Count);
    }

    [Fact]
    public void SwitchingModesResetsBrowseBoundaryAttempts()
    {
        var state = new NavigationSessionState();
        var root = new FolderTreeEntry("root", "root");
        var next = new FolderTreeEntry("next", "next");
        state.ReplaceRoots([root, next]); state.SetActiveFolder(root); state.ReplaceFiles([Files[0]]);
        var activations = 0;
        var controller = new NavigationController(state, _ => { activations++; return Task.CompletedTask; });
        for (var i = 0; i < 4; i++) controller.NavigateByOneFile(1);
        controller.SwitchViewingMode(ViewingMode.Gallery);
        controller.SwitchViewingMode(ViewingMode.Browse);
        controller.NavigateByOneFile(1);
        Assert.Equal(0, activations);
    }
}
