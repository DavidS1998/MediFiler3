using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class GalleryHoverSelectionTests
{
    private static readonly FileEntry[] Files = Enumerable.Range(0, 5)
        .Select(i => new FileEntry($"file-{i}", $"file-{i}", 1, DateTime.UnixEpoch)).ToArray();

    [Fact]
    public void StartingUnselectedAddsWithoutClearingAndRevisitsDoNotToggle()
    {
        var selection = new GallerySelectionState();
        selection.Select(Files, Files[4]);
        var gesture = new GalleryHoverSelection(selection);
        gesture.Visit(Files, Files[0], true); gesture.Visit(Files, Files[2], true);
        gesture.Visit(Files, Files[0], true);
        Assert.Equal(new[] { Files[0], Files[2], Files[4] }, selection.GetSelectedFiles(Files));
        Assert.Equal(Files[4].FullPath, selection.AnchorPath);
    }

    [Fact]
    public void StartingSelectedRemovesThroughoutGestureAndReleaseChoosesNewDirection()
    {
        var selection = new GallerySelectionState();
        selection.Select(Files, Files[0]); selection.Select(Files, Files[2], toggle: true);
        var gesture = new GalleryHoverSelection(selection);
        gesture.Visit(Files, Files[0], true); gesture.Visit(Files, Files[1], true); gesture.Visit(Files, Files[2], true);
        gesture.Visit(Files, Files[0], true);
        Assert.Equal(0, selection.Count);
        gesture.Visit(Files, Files[3], false);
        Assert.Equal(0, selection.Count);
        gesture.Visit(Files, Files[0], true);
        Assert.Equal(Files[0], Assert.Single(selection.GetSelectedFiles(Files)));
    }

    [Fact]
    public void OrdinaryToggleSelectionRemainsIndependentAfterAltIsReleased()
    {
        var selection = new GallerySelectionState();
        selection.Select(Files, Files[2]);
        var gesture = new GalleryHoverSelection(selection);
        gesture.Visit(Files, Files[0], alt: false);
        Assert.False(selection.IsSelected(Files[0]));
        selection.Select(Files, Files[0], toggle: true);
        Assert.True(selection.IsSelected(Files[0]));
        selection.Select(Files, Files[0], toggle: true);
        Assert.False(selection.IsSelected(Files[0]));
        gesture.Visit(Files, Files[2], alt: true);
        Assert.False(selection.IsSelected(Files[2]));
        gesture.Visit(Files, Files[2], alt: false);
        selection.Select(Files, Files[2], toggle: true);
        Assert.True(selection.IsSelected(Files[2]));
    }

    [Fact]
    public void ForeignAndRemovedFilesCannotBecomeCommandTargetsAndResetEndsGesture()
    {
        var selection = new GallerySelectionState();
        var gesture = new GalleryHoverSelection(selection);
        var foreign = new FileEntry("foreign", "foreign", 1, DateTime.UnixEpoch);
        gesture.Visit(Files, foreign, true);
        Assert.Equal(0, selection.Count);
        gesture.Visit(Files, Files[0], true);
        gesture.Reset(); selection.Reconcile(Files.Skip(1).ToArray());
        gesture.Visit(Files.Skip(1).ToArray(), Files[0], true);
        Assert.Equal(0, selection.Count);
        gesture.Visit(Files, Files[1], true);
        Assert.True(selection.IsSelected(Files[1]));
    }
}
