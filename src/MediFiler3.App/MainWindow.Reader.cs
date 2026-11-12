using MediFiler3.Application.Navigation;
using System.Windows.Input;

namespace MediFiler3;

public partial class MainWindow
{
    // Keep these Reader shortcuts dormant with their hidden toolbar actions. The shared
    // Browse/Gallery bindings and the underlying command APIs remain enabled.
    private static bool IsHiddenReaderCommandShortcut(Key key, ModifierKeys modifiers) =>
        modifiers == ModifierKeys.None && key is Key.Enter or Key.F2 or Key.Delete ||
        modifiers == ModifierKeys.Control && key is Key.Enter or Key.Z or Key.Y ||
        modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && key == Key.Enter;

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
