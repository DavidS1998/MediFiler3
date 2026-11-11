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
                var text = $"{wanted.Count} selected";
                if (_selectionStatus.Text != text)
                {
                    _selectionStatus.Text = text;
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

    private bool HandleSelectionKey(VirtualKey key, bool control)
    {
        if (!_active) return false;
        if (key == VirtualKey.A && control) { _selection.SelectAll(_files); return true; }
        if (key == VirtualKey.Escape) { _selection.Clear(); return true; }
        if (key == VirtualKey.Enter)
        {
            var target = _files.FirstOrDefault(file => StringComparer.OrdinalIgnoreCase.Equals(file.FullPath, _selection.FocusedPath));
            if (target is not null) RequestOpen(target);
            return true;
        }
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
        menu.Items.Add(new MenuFlyoutSeparator());
        var all = new MenuFlyoutItem { Text = "Select all", IsEnabled = _files.Count > 0 };
        var clear = new MenuFlyoutItem { Text = "Clear selection", IsEnabled = _selection.Count > 0 };
        all.Click += (_, _) => { if (Valid()) _selection.SelectAll(_files); };
        clear.Click += (_, _) => { if (Valid()) _selection.Clear(); };
        menu.Items.Add(all); menu.Items.Add(clear);
        return menu;
    }
}
