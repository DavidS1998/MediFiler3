using MediFiler3.Application.Navigation;

namespace MediFiler3;

public partial class MainWindow
{
    private bool _readerJumpOpen;
    private bool ReaderIsActive => PageContent.Content == _browseView && _browseView.IsReader;
    private bool ReaderCommandsAvailable => ReaderIsActive && IsEnabled && !OwnedWindows.OfType<System.Windows.Window>().Any(window => window.IsVisible);
    private void UpdateReaderNavigation() => ToolbarIsland.SetReaderNavigation(_browseView.ReaderPageIndex, _navigationState.Reader.Pages.Count);
    private async Task PromptReaderJumpAsync()
    {
        if (!ReaderCommandsAvailable || _readerJumpOpen) return;
        var root = _navigationState.ActiveRoot; var folder = _navigationState.ActiveFolder;
        var pages = _navigationState.Reader.Pages.ToArray();
        if (pages.Length == 0) return;
        _readerJumpOpen = true;
        try
        {
            var index = await ToolbarIsland.PromptReaderPageAsync(pages, _browseView.ReaderPageIndex);
            if (!ReaderIsActive || !ReferenceEquals(root, _navigationState.ActiveRoot) || !ReferenceEquals(folder, _navigationState.ActiveFolder) ||
                !pages.SequenceEqual(_navigationState.Reader.Pages)) return;
            if (index is { } page) _browseView.JumpReaderPage(page);
            _browseView.FocusReader();
        }
        finally { _readerJumpOpen = false; }
    }
}
