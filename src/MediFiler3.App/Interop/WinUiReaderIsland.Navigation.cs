using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using System.Windows.Input;
using Xaml = Microsoft.UI.Xaml;

namespace MediFiler3.Interop;

public sealed partial class WinUiReaderIsland
{
    private FileEntry? _explicitPage;
    private bool _endRequested;
    private long _interactionVersion;
    private double _explicitScrollOffset = double.NaN;
    public event Action<FileEntry>? OpenInBrowseRequested;
    public int CurrentPageIndex => _explicitPage is not null && session.Pages.Contains(_explicitPage)
        ? session.Pages.ToList().IndexOf(_explicitPage)
        : session.IndexAt(_scroll is null ? session.Offset(session.Anchor) : _scroll.VerticalOffset + _scroll.ViewportHeight * .25);

    public bool JumpToPage(int index)
    {
        if (!_active || !session.SelectPage(index)) return false;
        _endRequested = false; _explicitScrollOffset = double.NaN; _explicitPage = session.Pages[index]; _reported = null; _restore = true; QueueUpdate(); return true;
    }
    public bool Navigate(ReaderNavigation command)
    {
        if (!_active || _scroll is null) return false;
        if (!_restore) session.Capture(_scroll.VerticalOffset);
        if (command is ReaderNavigation.PreviousImage or ReaderNavigation.NextImage)
            return JumpToPage(Math.Clamp(CurrentPageIndex + (command == ReaderNavigation.PreviousImage ? -1 : 1), 0, Math.Max(0, session.Pages.Count - 1)));
        if (!session.Navigate(command, _scroll.ViewportHeight, CurrentPageIndex)) return false;
        _endRequested = command == ReaderNavigation.End;
        _explicitPage = null; _restore = true; QueueUpdate(); return true;
    }
    public bool HandleNavigationShortcut(Key key, ModifierKeys modifiers)
    {
        if (key == Key.R && modifiers == ModifierKeys.Control)
        {
            var index = CurrentPageIndex;
            if (_active && index >= 0)
            {
                var file = session.Pages[index]; var version = _interactionVersion;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_active && version == _interactionVersion && session.Pages.Contains(file)) Invalidate([file.FullPath]);
                }));
            }
            return _active;
        }
        ReaderNavigation? command = modifiers == ModifierKeys.None ? key switch
        {
            Key.Left => ReaderNavigation.PreviousImage, Key.Right => ReaderNavigation.NextImage,
            Key.Up => ReaderNavigation.LineUp, Key.Down => ReaderNavigation.LineDown,
            Key.PageUp => ReaderNavigation.ViewUp, Key.PageDown or Key.Space => ReaderNavigation.ViewDown,
            Key.Home => ReaderNavigation.Start, Key.End => ReaderNavigation.End, _ => null
        } : modifiers == ModifierKeys.Shift && key == Key.Space ? ReaderNavigation.ViewUp : null;
        if (command is null) return false;
        // Consume the shortcut at the empty/boundary state rather than editing the focused slider/tree.
        Navigate(command.Value); return true;
    }
    public void OpenCurrentInBrowse()
    {
        var index = CurrentPageIndex;
        if (!_active || index < 0) return;
        var file = session.Pages[index]; var version = _interactionVersion;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_active && version == _interactionVersion && session.Pages.Contains(file)) OpenInBrowseRequested?.Invoke(file);
        }));
    }
    public void FocusPages() => _scroll?.Focus(Xaml.FocusState.Programmatic);
    private void OnReaderInput()
    {
        var pending = _restore;
        _endRequested = false; _restore = false;
        // A click or Tab without scrolling must not change an explicitly selected small page.
        // Actual scroll changes are detected in ViewChanged, including UIA scroll requests.
        if (pending) { _explicitPage = null; PublishCurrent(); }
    }
    private void OnReaderViewChanged()
    {
        if (!_active || _updating || _scroll is null) return;
        // Native/UIA scroll requests need not produce a pointer event.
        if (!_restore && (_explicitPage is not null || _endRequested) && double.IsFinite(_explicitScrollOffset) &&
            Math.Abs(_scroll.VerticalOffset - _explicitScrollOffset) > .5) { _explicitPage = null; _endRequested = false; }
        PublishCurrent(); QueueUpdate();
    }
    private void QueueRowAction(Row row, Action action) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (Live(row)) action();
    }));
}
