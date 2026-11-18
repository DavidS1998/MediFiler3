using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Interop;
using System.Windows;

namespace MediFiler3.Views;

public partial class BrowseView
{
    public Func<System.Windows.Input.Key, System.Windows.Input.ModifierKeys, bool, bool>? ViewShortcutHandler { get; set; }
    private WinUiReaderIsland? _reader;
    private IReaderImageService? _readerImages;
    private string? _lastReaderCurrent;
    public bool IsReader => _navigationState.ViewingMode == ViewingMode.Reader;
    public bool IsBrowse => _navigationState.ViewingMode == ViewingMode.Browse;

    public void SetReaderOptions(ReaderOptions options)
    {
        options.Validate();
        if (_reader is not null) _reader.SetOptions(options);
        else _navigationState.Reader.SetOptions(options);
    }

    public int ReaderPageIndex => _reader?.CurrentPageIndex ?? -1;
    public bool NavigateReader(ReaderNavigation command) => IsReader && _viewerActive && _reader?.Navigate(command) == true;
    public bool JumpReaderPage(int index) => IsReader && _viewerActive && _reader?.JumpToPage(index) == true;
    public void FocusReader() { if (IsReader && _viewerActive) _reader?.FocusPages(); }
    public void OpenReaderInBrowse() { if (IsReader && _viewerActive) _reader?.OpenCurrentInBrowse(); }

    private void ApplyReaderMode()
    {
        var active = IsReader && _viewerActive;
        ReaderHost.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        if (active && _reader is null)
        {
            _readerImages ??= new FileSystemReaderImageService(new FileSystemFileContentService());
            _reader = new WinUiReaderIsland(_navigationState.Reader, _readerImages);
            _reader.ViewShortcutHandler = (key, modifiers, repeat) => ViewShortcutHandler?.Invoke(key, modifiers, repeat) ?? false;
            _reader.CurrentPageChanged += file =>
            {
                if (!IsReader || !_viewerActive || _readerReconciliationDepth > 0 || !_navigationState.ContainsFile(file)) return;
                _lastReaderCurrent = file.FullPath;
                _navigationState.SetCurrentFile(file);
            };
            _reader.OpenInBrowseRequested += file =>
            {
                if (!IsReader || !_viewerActive || _readerReconciliationDepth > 0 || !_navigationState.ContainsFile(file)) return;
                if (_navigationState.CurrentFile != file) _reader.JumpToPage(_navigationState.Reader.Pages.ToList().IndexOf(file));
                _lastReaderCurrent = file.FullPath;
                _navigationState.SetCurrentFile(file);
                BrowseRequested?.Invoke();
            };
            ReaderHost.Children.Add(_reader);
        }
        if (active && _reader is not null)
        {
            var prefer = !StringComparer.OrdinalIgnoreCase.Equals(_lastReaderCurrent, _navigationState.CurrentFile?.FullPath);
            _reader.SetFilterNotice(_fileQuery != MediFiler3.Application.Files.FileQuery.AllFiles, _inventoryIncomplete, _inventoryDiscovering);
            SetReaderListing(prefer);
            _reader.SetTreeInset(_folderOverlayPinned ? FolderOverlay.Width + FolderOverlay.Margin.Left : 26);
        }
        _reader?.SetActive(active);
        UpdateReaderExternalApplication();
    }

    private bool _readerPreferCurrent;
    private bool _readerUseFirstMatch;
    private bool _readerKeepQueryPosition;
    private int _readerReconciliationDepth;
    private void RefreshReaderFiles()
    {
        if (!IsReader || !_viewerActive || _reader is null || _readerReconciliationDepth > 0) return;
        var prefer = _readerPreferCurrent; _readerPreferCurrent = false;
        SetReaderListing(prefer);
    }

    private void SetReaderListing(bool prefer)
    {
        var firstOnMissing = _readerUseFirstMatch;
        _readerUseFirstMatch = false;
        var keepPosition = _readerKeepQueryPosition;
        _readerKeepQueryPosition = false;
        _reader?.SetFiles(_navigationState.ActiveFolder?.Path, _navigationState.Files, _navigationState.CurrentFile,
            prefer && !firstOnMissing && !keepPosition, firstOnMissing, keepPosition);
    }
    private IDisposable DeferReaderReconciliation()
    {
        _readerReconciliationDepth++;
        return new ReaderReconciliationDeferral(this);
    }
    private sealed class ReaderReconciliationDeferral(BrowseView owner) : IDisposable
    {
        public void Dispose() { owner._readerReconciliationDepth--; owner.RefreshReaderFiles(); }
    }

}
