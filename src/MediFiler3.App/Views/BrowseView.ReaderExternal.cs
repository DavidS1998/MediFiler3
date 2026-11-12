using System.Windows;
using System.Windows.Interop;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private readonly IExternalFileService _readerExternalFiles;
    private readonly DefaultApplicationResolver _readerDefaultApplication;
    private FileEntry? _readerAssociationFile;
    private bool _readerExternalBusy;
    private FileEntry? ReaderExternalFile => IsReader && _viewerActive && _navigationState.CurrentFile is { } file &&
        _navigationState.ContainsFile(file) && ReaderSessionState.Supports(file) ? file : null;
    private ExternalOpenState ReaderExternalOpenState => new(ReaderExternalFile is not null, _readerExternalBusy,
        _readerAssociationFile == ReaderExternalFile ? _readerDefaultApplication.Application : null);

    private void UpdateReaderExternalApplication()
    {
        var file = ReaderExternalFile;
        if (_readerAssociationFile == file) return;
        _readerAssociationFile = file;
        if (file is null) _readerDefaultApplication.Clear();
        else _ = _readerDefaultApplication.ResolveAsync(file.FullPath);
    }

    private async Task OpenReaderExternallyAsync(ExternalFileAction action)
    {
        var file = ReaderExternalFile;
        var window = Window.GetWindow(this);
        if (file is null || _readerExternalBusy || _readerReconciliationDepth > 0 ||
            _navigationState.Reader.Pages.ElementAtOrDefault(ReaderPageIndex) != file ||
            window is { IsEnabled: false } || window?.OwnedWindows.OfType<Window>().Any(owner => owner.IsVisible) == true ||
            action == ExternalFileAction.DefaultApplication && !ReaderExternalOpenState.CanOpenDefault) return;
        var root = _navigationState.ActiveRoot; var folder = _navigationState.ActiveFolder;
        bool Current() => ReaderExternalFile == file && ReferenceEquals(root, _navigationState.ActiveRoot) && ReferenceEquals(folder, _navigationState.ActiveFolder);
        using var deferral = DeferReconciliation();
        _readerExternalBusy = true; PresentationChanged?.Invoke();
        try
        {
            var result = await _readerExternalFiles.OpenAsync(file.FullPath, action, window is null ? 0 : new WindowInteropHelper(window).Handle);
            if (!Current()) return;
            ShowOperationNotice(result.Message);
            await _readerDefaultApplication.ResolveAsync(file.FullPath);
        }
        catch (Exception exception)
        {
            if (Current()) ShowOperationNotice($"Could not open the file: {exception.Message}");
        }
        finally { _readerExternalBusy = false; PresentationChanged?.Invoke(); }
    }
}
