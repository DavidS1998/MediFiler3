using System.Windows;
using System.Windows.Controls;
using MediFiler3.Application.Folders;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private sealed record GalleryMoveDrag(string Token, FolderTreeEntry Root, FolderTreeEntry Folder,
        long Revision, FileEntry[] Files, IDisposable Deferral);
    private GalleryMoveDrag? _galleryMoveDrag;
    public bool IsGalleryMoveDragActive => _galleryMoveDrag is not null;
    private TreeViewItem? _galleryDropTarget;
    public static readonly DependencyProperty IsGalleryDropTargetProperty = DependencyProperty.RegisterAttached(
        "IsGalleryDropTarget", typeof(bool), typeof(BrowseView), new PropertyMetadata(false));
    public static bool GetIsGalleryDropTarget(DependencyObject target) => (bool)target.GetValue(IsGalleryDropTargetProperty);
    public static void SetIsGalleryDropTarget(DependencyObject target, bool value) => target.SetValue(IsGalleryDropTargetProperty, value);

    private string? BeginGalleryMoveDrag(IReadOnlyList<FileEntry> files)
    {
        EndGalleryMoveDrag();
        if (!IsGallery || !_viewerActive || _historyBusy || _galleryCommandCancellation is not null || files.Count == 0 ||
            files.Any(file => !_navigationState.ContainsFile(file)) || _navigationState.ActiveRoot is not { } root ||
            _navigationState.ActiveFolder is not { } folder) return null;
        var deferral = DeferReconciliation();
        var token = "MediFiler3-GalleryMove:" + Guid.NewGuid().ToString("N");
        _galleryMoveDrag = new(token, root, folder, _commandRevision, files.ToArray(), deferral);
        SetFolderOverlayVisible(true);
        ShowOperationNotice($"Dragging {files.Count} {(files.Count == 1 ? "file" : "files")}. Drop on a folder to move; Escape cancels.");
        StartGalleryDragWheelHook();
        return token;
    }

    private bool GalleryDragIsCurrent() => _galleryMoveDrag is { } drag && IsGallery && _viewerActive &&
        !_historyBusy && _galleryCommandCancellation is null && drag.Revision == _commandRevision &&
        ReferenceEquals(drag.Root, _navigationState.ActiveRoot) && ReferenceEquals(drag.Folder, _navigationState.ActiveFolder) &&
        drag.Files.All(_navigationState.ContainsFile);

    private bool AcceptGalleryDragData(IDataObject data) => GalleryDragIsCurrent() &&
        data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string token && token == _galleryMoveDrag!.Token;

    private void HighlightGalleryDropTarget(TreeViewItem? item)
    {
        if (ReferenceEquals(item, _galleryDropTarget)) return;
        if (_galleryDropTarget is not null) SetIsGalleryDropTarget(_galleryDropTarget, false);
        _galleryDropTarget = item;
        if (item is not null) SetIsGalleryDropTarget(item, true);
    }

    private void OnGalleryTreeDragOver(object sender, DragEventArgs args)
    {
        // Leave existing Explorer folder loading to the window.
        if (_galleryMoveDrag is null) return;
        args.Handled = true; args.Effects = DragDropEffects.None;
        var item = FindAncestor<TreeViewItem>(args.OriginalSource as DependencyObject);
        var authorized = AcceptGalleryDragData(args.Data) && args.AllowedEffects.HasFlag(DragDropEffects.Move);
        UpdateGalleryTreeDragNavigation(authorized, args.GetPosition(FolderTree));
        var valid = authorized &&
            item?.DataContext is FolderTreeEntry folder && _navigationState.GetRootForFolder(folder) is not null &&
            !PathsEqual(folder.Path, _galleryMoveDrag.Folder.Path);
        HighlightGalleryDropTarget(valid ? item : null);
        if (valid) args.Effects = DragDropEffects.Move;
    }

    private void OnGalleryTreeDragLeave(object sender, DragEventArgs args)
    {
        HighlightGalleryDropTarget(null);
        // OLE DragLeave can carry the last in-tree position. A subsequent enter/over restarts navigation.
        StopGalleryTreeDragNavigation();
    }

    private async void OnGalleryTreeDrop(object sender, DragEventArgs args)
    {
        if (_galleryMoveDrag is null) return;
        OnGalleryTreeDragOver(sender, args);
        var destination = _galleryDropTarget?.DataContext as FolderTreeEntry;
        var files = _galleryMoveDrag.Files;
        var accepted = args.Effects == DragDropEffects.Move && destination is not null;
        EndGalleryMoveDrag();
        if (accepted) await RunGalleryFileCommandAsync(destination, files);
    }

    private void EndGalleryMoveDrag()
    {
        StopGalleryDragWheelHook();
        StopGalleryTreeDragNavigation();
        HighlightGalleryDropTarget(null);
        var drag = _galleryMoveDrag; _galleryMoveDrag = null;
        drag?.Deferral.Dispose();
        if (drag is not null) _gallery?.ClearMoveDragVisual();
        if (drag is not null) SetFolderOverlayVisible(_folderOverlayPinned || _folderHoverReveal);
    }
}
