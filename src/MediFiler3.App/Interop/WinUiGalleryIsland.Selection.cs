using MediFiler3.Domain.Files;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.System;

namespace MediFiler3.Interop;

public sealed partial class WinUiGalleryIsland
{
    private MenuFlyout? _selectionMenu;
    private long _selectionContextVersion;
    private readonly List<TextBlock> _selectionOutlines = [];

    private Grid CreateSelectionStatus()
    {
        var host = new Grid { HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Bottom,
            Margin = new Thickness(8, 8, 24, 8), IsHitTestVisible = false };
        for (var y = -1; y <= 1; y++)
            for (var x = -1; x <= 1; x++)
            {
                if (x == 0 && y == 0) continue;
                var outline = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Black),
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    RenderTransform = new Microsoft.UI.Xaml.Media.TranslateTransform { X = x, Y = y } };
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(outline, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
                _selectionOutlines.Add(outline); host.Children.Add(outline);
            }
        _selectionStatus = new TextBlock { Foreground = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetLiveSetting(_selectionStatus, Microsoft.UI.Xaml.Automation.Peers.AutomationLiveSetting.Polite);
        host.Children.Add(_selectionStatus);
        return host;
    }

    private static bool ControlHeld() => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control) &
        Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;
    private static bool AltHeld() => (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Menu) &
        Windows.UI.Core.CoreVirtualKeyStates.Down) != 0;

    private void PaintHoveredTile(bool alt)
    {
        if (_dragCaption is not null) return;
        if (!alt) { _hoverSelection.Reset(); return; }
        if (_active && _hoveredRoot is not null && FileFromElement(_hoveredRoot) is { } file)
            _hoverSelection.Visit(_files, file, alt: true);
    }

    private void OnTilePointerMoved(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs args)
    {
        if (!_active || _startingMoveDrag || _dragCaption is not null || sender is not Grid root) return;
        if (_hoveredRoot != root)
        {
            if (_hoveredRoot is not null) SetTileHover(_hoveredRoot, false);
            _hoveredRoot = root; SetTileHover(root, true);
        }
        PaintHoveredTile(AltHeld());
    }
    // Native Extended selection owns pointer/range/arrow behavior and exposes the
    // standard UI Automation Selection/SelectionItem patterns. Session state owns
    // the path-based snapshot used by commands and filesystem reconciliation.
    private void ApplySelection()
    {
        if (_grid is null) return;
        var wasUpdating = _updating;
        _updating = true;
        try
        {
            var wanted = _selection.GetSelectedFiles(_files).ToHashSet();
            // Bulk clear avoids thousands of WinRT removals when clearing Select All.
            if (wanted.Count == 0) _grid.SelectedItems.Clear();
            else
                foreach (var file in _grid.SelectedItems.OfType<FileEntry>().ToArray())
                    if (!wanted.Contains(file)) _grid.SelectedItems.Remove(file);
            if (wanted.Count == _files.Count && wanted.Count > 0 && _grid.SelectedItems.Count != wanted.Count) _grid.SelectAll();
            else
            {
                var existing = _grid.SelectedItems.OfType<FileEntry>().ToHashSet();
                foreach (var file in wanted)
                    if (!existing.Contains(file)) _grid.SelectedItems.Add(file);
            }
            UpdateSelectionBorders();
            if (_selectionStatus is not null)
            {
                var text = _dragCaption ?? $"{wanted.Count} selected";
                if (_selectionStatus.Text != text)
                {
                    _selectionStatus.Text = text;
                    foreach (var outline in _selectionOutlines) outline.Text = text;
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_selectionStatus, text);
                    Microsoft.UI.Xaml.Automation.Peers.FrameworkElementAutomationPeer.FromElement(_selectionStatus)?
                        .RaiseAutomationEvent(Microsoft.UI.Xaml.Automation.Peers.AutomationEvents.LiveRegionChanged);
                }
            }
        }
        finally { _updating = wasUpdating; }
    }

    private void OnSelectionChanged()
    {
        ApplySelection();
        if (_updating || !_active) return;
        CancelPendingReveal();
        var file = _files.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _selection.FocusedPath));
        if (file is null) return;
        _current = file;
        Forward(() =>
        {
            // A queued focus notification must not overwrite newer input or a removed file.
            if (StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _selection.FocusedPath) && _files.Contains(file))
                FileSelected?.Invoke(file);
        });
    }

    private FileEntry? FocusedFile()
    {
        if (_grid?.XamlRoot is null) return null;
        return FileFromElement(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(_grid.XamlRoot) as DependencyObject);
    }

    private FileEntry? FileFromElement(DependencyObject? element)
    {
        while (element is not null && element is not SelectorItem)
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        return element is SelectorItem item && _tiles.TryGetValue(item, out var tile) ? tile.File : null;
    }

    public bool HandleNavigationShortcut(System.Windows.Input.Key key, System.Windows.Input.ModifierKeys modifiers)
    {
        if (!_active || _grid is null || modifiers.HasFlag(System.Windows.Input.ModifierKeys.Alt)) return false;
        if (key is not (System.Windows.Input.Key.Left or System.Windows.Input.Key.Right or System.Windows.Input.Key.Up or
            System.Windows.Input.Key.Down or System.Windows.Input.Key.Home or System.Windows.Input.Key.End or
            System.Windows.Input.Key.PageUp or System.Windows.Input.Key.PageDown or System.Windows.Input.Key.Space)) return false;
        var focus = GalleryGetFocus();
        if (focus == Handle || GalleryIsChild(Handle, focus)) return false; // Retain native GridView modifier/navigation semantics.
        if (_files.Count == 0) return true;
        var index = Math.Max(0, _files.ToList().FindIndex(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _selection.FocusedPath)));
        var columns = Math.Max(1, (int)Math.Floor(Math.Max(0, _grid.ActualWidth - _treeInset - 4) / (_tileWidth + 2)));
        var page = columns * Math.Max(1, (int)Math.Floor(_grid.ActualHeight / (_tileHeight + 2)));
        var target = key switch
        {
            System.Windows.Input.Key.Left => index - 1, System.Windows.Input.Key.Right => index + 1,
            System.Windows.Input.Key.Up => index - columns, System.Windows.Input.Key.Down => index + columns,
            System.Windows.Input.Key.PageUp => index - page, System.Windows.Input.Key.PageDown => index + page,
            System.Windows.Input.Key.Home => 0, System.Windows.Input.Key.End => _files.Count - 1, _ => index
        };
        var file = _files[Math.Clamp(target, 0, _files.Count - 1)];
        var control = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
        var shift = modifiers.HasFlag(System.Windows.Input.ModifierKeys.Shift);
        if (control && !shift && key != System.Windows.Input.Key.Space) _selection.Focus(_files, file);
        else _selection.Select(_files, file, toggle: control, range: shift);
        RevealCurrentFile(file, preserveSelection: true);
        return true;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "GetFocus")]
    private static extern nint GalleryGetFocus();
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsChild")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GalleryIsChild(nint parent, nint child);

    private bool HandleSelectionKey(VirtualKey key, bool control)
    {
        if (_dragCaption is not null) return false;
        if (!_active) return false;
        if (key == VirtualKey.A && control) { _selection.SelectAll(_files); return true; }
        if (key == VirtualKey.Escape) { _selection.Clear(); return true; }
        if (key == VirtualKey.Enter)
        {
            var target = _files.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _selection.FocusedPath));
            if (target is not null) RequestOpen(target);
            return true;
        }
        if (key == VirtualKey.F2) { Forward(() => RenameRequested?.Invoke()); return true; }
        if (key == VirtualKey.Delete && !control) { Forward(() => DeleteRequested?.Invoke()); return true; }
        if (key == VirtualKey.Z && control) { Forward(() => UndoRequested?.Invoke()); return true; }
        if (key == VirtualKey.Y && control) { Forward(() => RedoRequested?.Invoke()); return true; }
        if (key == VirtualKey.F5) { Forward(() => RefreshRequested?.Invoke()); return true; }
        return false; // Keep native arrows, Home/End, PageUp/Down, Space and modifier behavior.
    }

    private void OnGalleryContextRequested(UIElement sender, Microsoft.UI.Xaml.Input.ContextRequestedEventArgs args)
    {
        if (!_active || _grid is null) return;
        var positioned = args.TryGetPosition(_grid, out var point);
        var target = positioned ? FileFromElement(args.OriginalSource as DependencyObject) : FocusedFile();
        var menu = CreateSelectionMenu(target);
        args.Handled = true;
        _selectionMenu?.Hide();
        _selectionMenu = menu;
        menu.Closed += (_, _) => { if (ReferenceEquals(_selectionMenu, menu)) _selectionMenu = null; };
        if (positioned) menu.ShowAt(_grid, point);
        else menu.ShowAt(_grid);
    }

    private MenuFlyout CreateSelectionMenu(FileEntry? target)
    {
        if (target is not null)
        {
            if (!_selection.IsSelected(target)) _selection.Select(_files, target);
            else _selection.Focus(_files, target);
        }
        var activity = _activityVersion;
        var context = _selectionContextVersion;
        bool Valid() => _active && activity == _activityVersion && context == _selectionContextVersion;
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = "Open in Browse", IsEnabled = target is not null };
        open.Click += (_, _) => { if (Valid() && target is not null) RequestOpen(target); };
        menu.Items.Add(open);
        var recycle = new MenuFlyoutItem { Text = "Send selected files to Recycle Bin", IsEnabled = _selection.Count > 0 };
        recycle.Click += (_, _) => { if (Valid()) Forward(() => { if (Valid()) DeleteRequested?.Invoke(); }); };
        menu.Items.Add(recycle);
        var rename = new MenuFlyoutItem { Text = "Rename", IsEnabled = _selection.Count == 1 };
        rename.Click += (_, _) => { if (Valid()) Forward(() => { if (Valid()) RenameRequested?.Invoke(); }); };
        menu.Items.Add(rename);
        menu.Items.Add(new MenuFlyoutSeparator());
        var all = new MenuFlyoutItem { Text = "Select all", IsEnabled = _files.Count > 0 };
        var clear = new MenuFlyoutItem { Text = "Clear selection", IsEnabled = _selection.Count > 0 };
        all.Click += (_, _) => { if (Valid()) _selection.SelectAll(_files); };
        clear.Click += (_, _) => { if (Valid()) _selection.Clear(); };
        menu.Items.Add(all); menu.Items.Add(clear);
        return menu;
    }
}
