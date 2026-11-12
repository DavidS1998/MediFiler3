using System.Windows;
using System.Windows.Input;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Viewing;

namespace MediFiler3.Views;

public partial class BrowseView
{
    public bool HandleViewShortcut(Key key, ModifierKeys modifiers)
    {
        if (!_viewerActive) return false;
        if (IsReader) return _reader?.HandleNavigationShortcut(key, modifiers) == true;
        if (IsGallery)
        {
            if (key == Key.A && modifiers == ModifierKeys.Control) _navigationState.GallerySelection.SelectAll(_navigationState.Files);
            else if (key == Key.Escape && modifiers == ModifierKeys.None) _navigationState.GallerySelection.Clear();
            else if (key == Key.Enter && modifiers == ModifierKeys.None)
            {
                var file = _navigationState.Files.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _navigationState.GallerySelection.FocusedPath));
                if (file is not null) { _navigationState.SetCurrentFile(file); BrowseRequested?.Invoke(); }
            }
            else return _gallery?.HandleNavigationShortcut(key, modifiers) == true;
            return true;
        }
        var source = PresentationSource.FromVisual(this);
        if (source is null) return false;
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        if (ViewerHost.HandleKey(args)) return true;
        if (modifiers == ModifierKeys.None && key is Key.Left or Key.Right or Key.Up or Key.Down &&
            ViewerInputPolicy.CanNavigateWithArrow(false, ViewerHost.Capabilities, ViewerHost.Interaction))
        {
            NavigateByOneFile(key is Key.Left or Key.Up ? -1 : 1);
            return true;
        }
        return false;
    }
}
