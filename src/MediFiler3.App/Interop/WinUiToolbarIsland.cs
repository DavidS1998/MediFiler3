using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Graphics;
using MediFiler3.Application.Operations;
using Thickness = Microsoft.UI.Xaml.Thickness;
using GridLength = Microsoft.UI.Xaml.GridLength;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;

namespace MediFiler3.Interop;

public sealed class WinUiToolbarIsland : HwndHost, IWinUiDialogService
{
    private DesktopWindowXamlSource? _xamlSource;
    private TextBlock? _folderDetails;
    private TextBlock? _fileMetadata;
    private string _fileMetadataText = string.Empty;
    private ImageToolbarGroup? _imageToolbar;
    private TextToolbarGroup? _textToolbar;
    private MediaToolbarGroup? _mediaToolbar;
    private bool _mediaControlsVisible;
    private MediFiler3.Application.Viewing.MediaPlaybackState _mediaState = MediFiler3.Application.Viewing.MediaPlaybackState.Empty;
    private ExternalOpenToolbarGroup? _externalToolbar;
    private MediFiler3.Application.Files.ExternalOpenState _externalState = new(false, false, null);
    private bool _textControlsVisible;
    private bool _textScrolling;
    private bool _canPreviousText;
    private bool _canNextText;
    private bool _imageControlsVisible;
    private bool _imageControlsEnabled;
    private Grid? _rootElement;
    private AppBarButton? _renameFileButton;
    private AppBarButton? _deleteFileButton;
    private AppBarButton? _undoButton;
    private AppBarButton? _redoButton;
    private string _folderTitleText = "MediFiler3";
    private string _folderDetailsText = "No folder loaded";
    private double _zoomLevel = 1d;
    private bool _fileActionsEnabled;

    public event Action? LoadFoldersRequested;
    public event Action? ResetZoomRequested;
    public event Action? ZoomInRequested;
    public event Action? ZoomOutRequested;
    public event Action? RenameFileRequested;
    public event Action? DeleteFileRequested;
    public event Action? UndoRequested;
    public event Action? RedoRequested;
    public event Action? ToggleTextInteractionRequested;
    public event Action? PreviousTextWindowRequested;
    public event Action? NextTextWindowRequested;
    public event Action? OpenDefaultRequested;
    public event Action? OpenWithRequested;
    public event Action? TogglePlaybackRequested;
    public event Action? StopPlaybackRequested;
    public event Action<double>? SeekPlaybackRequested;
    public event Action<double>? PlaybackVolumeRequested;
    public event Action? TogglePlaybackMuteRequested;
    public event Action<double>? JumpPlaybackRequested;

    public Task<string?> PromptTextAsync(string title, string message, string initialValue = "", string primaryAction = "OK")
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null) return Task.FromResult<string?>(null);

        var window = new System.Windows.Window
        {
            Title = title,
            Width = 480,
            Height = 220,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0))
        };
        var host = new WinUiDialogIsland();
        window.Content = host;
        string? result = null;
        window.ContentRendered += (_, _) =>
        {
            var content = new StackPanel
            {
                Spacing = 14,
                VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
            };
            content.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Foreground = Brush(231, 232, 234)
            });
            var input = new Microsoft.UI.Xaml.Controls.TextBox
            {
                Text = initialValue,
                MinWidth = 380,
                SelectionStart = initialValue.Length
            };
            content.Children.Add(input);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
                Spacing = 8
            };
            var cancel = new Microsoft.UI.Xaml.Controls.Button { Content = "Cancel", MinWidth = 90 };
            cancel.Click += (_, _) => window.Close();
            var confirm = new Microsoft.UI.Xaml.Controls.Button
            {
                Content = primaryAction,
                MinWidth = 90
            };
            confirm.Click += (_, _) =>
            {
                result = input.Text.Trim();
                window.Close();
            };
            input.KeyDown += (_, args) =>
            {
                if (args.Key != Windows.System.VirtualKey.Enter) return;
                result = input.Text.Trim();
                window.Close();
                args.Handled = true;
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            content.Children.Add(buttons);
            host.RootElement!.Children.Add(new Border
            {
                Padding = new Microsoft.UI.Xaml.Thickness(24),
                Child = content
            });
            input.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            input.SelectAll();
        };
        window.ShowDialog();
        return Task.FromResult(result);
    }

    public Task<bool> ConfirmAsync(string title, string message, string confirmAction)
    {
        var owner = System.Windows.Window.GetWindow(this);
        if (owner is null) return Task.FromResult(false);

        var window = CreateDialogWindow(owner, title, 540, 240);
        var host = new WinUiDialogIsland();
        window.Content = host;
        var confirmed = false;
        window.ContentRendered += (_, _) =>
        {
            var content = new StackPanel { Spacing = 18, VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center };
            content.Children.Add(new TextBlock
            {
                Text = message,
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Foreground = Brush(231, 232, 234)
            });
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
                Spacing = 8
            };
            var cancel = new Microsoft.UI.Xaml.Controls.Button { Content = "Cancel", MinWidth = 100 };
            cancel.Click += (_, _) => window.Close();
            var confirm = new Microsoft.UI.Xaml.Controls.Button { Content = confirmAction, MinWidth = 150 };
            confirm.Click += (_, _) =>
            {
                confirmed = true;
                window.Close();
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(confirm);
            content.Children.Add(buttons);
            host.RootElement!.Children.Add(new Border
            {
                Padding = new Microsoft.UI.Xaml.Thickness(24),
                Child = content
            });
        };
        window.ShowDialog();
        return Task.FromResult(confirmed);
    }

    private static System.Windows.Window CreateDialogWindow(System.Windows.Window owner, string title, double width, double height) =>
        new()
        {
            Title = title,
            Width = width,
            Height = height,
            ResizeMode = System.Windows.ResizeMode.NoResize,
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            Owner = owner,
            ShowInTaskbar = false,
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 0, 0))
        };

    public void ShowFolderMenu(System.Windows.Point screenPoint, string folderName, Func<string, Task> actionHandler)
    {
        if (_xamlSource is null || _rootElement is null) return;

        var islandHandle = Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId);
        if (!GetWindowRect(islandHandle, out var islandRect)) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        var position = new Windows.Foundation.Point(
            (screenPoint.X - islandRect.Left) / dpi.DpiScaleX,
            (screenPoint.Y - islandRect.Top) / dpi.DpiScaleY);

        var menu = new MenuFlyout();
        AddFolderMenuItem(menu, "New folder", "new-folder", folderName, actionHandler);
        AddFolderMenuItem(menu, "Rename folder", "rename-folder", folderName, actionHandler);
        AddFolderMenuItem(menu, "Move folder toâ€¦", "move-folder", folderName, actionHandler);
        AddFolderMenuItem(menu, "Send folder to Recycle Bin", "recycle-folder", folderName, actionHandler);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddFolderMenuItem(menu, "Refresh tree", "refresh-tree", folderName, actionHandler);
        AddFolderMenuItem(menu, "Open in File Explorer", "open-explorer", folderName, actionHandler);
        AddFolderMenuItem(menu, "Properties", "properties", folderName, actionHandler);
        menu.ShowAt(_rootElement, new FlyoutShowOptions { Position = position, Placement = FlyoutPlacementMode.Bottom });
    }

    public void SetFileActionsEnabled(bool enabled)
    {
        _fileActionsEnabled = enabled;
        if (_renameFileButton is not null) _renameFileButton.IsEnabled = enabled;
        if (_deleteFileButton is not null) _deleteFileButton.IsEnabled = enabled;
    }

    public void SetHistoryAvailability(bool canUndo, bool canRedo,
        FileOrganizationHistoryPreview? undoPreview = null, FileOrganizationHistoryPreview? redoPreview = null)
    {
        if (_undoButton is not null)
        {
            _undoButton.IsEnabled = canUndo;
            ToolTipService.SetToolTip(_undoButton, CreateHistoryToolTip("Undo", undoPreview));
        }

        if (_redoButton is not null)
        {
            _redoButton.IsEnabled = canRedo;
            ToolTipService.SetToolTip(_redoButton, CreateHistoryToolTip("Redo", redoPreview));
        }
    }

    private static ToolTip CreateHistoryToolTip(string action, FileOrganizationHistoryPreview? preview)
    {
        if (preview is null)
            return new ToolTip { Content = $"Nothing to {action.ToLowerInvariant()}." };

        const int maxItems = 8;
        var lines = preview.Items.Take(maxItems).Select(item =>
            $"{item.Action}: {item.SourcePath ?? "â€”"} â†’ {item.DestinationPath ?? "â€”"}").ToList();
        lines.Insert(0, $"{action} {preview.Items.Count} {(preview.Items.Count == 1 ? "change" : "changes")}:");
        if (preview.Items.Count > maxItems)
            lines.Add($"â€¦ and {preview.Items.Count - maxItems} more");

        return new ToolTip
        {
            MaxWidth = 560,
            MaxHeight = 480,
            Content = new TextBlock
            {
                Text = string.Join(Environment.NewLine, lines),
                TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                Foreground = Brush(231, 232, 234),
                MaxWidth = 540
            }
        };
    }

    public void SetFolderContext(string title, string details)
    {
        _folderTitleText = title;
        _folderDetailsText = details;
        UpdateFolderPath();
    }

    private void UpdateFolderPath()
    {
        if (_folderDetails is null) return;
        _folderDetails.Inlines.Clear();
        var trimmed = _folderDetailsText.TrimEnd('\\', '/');
        var name = _folderTitleText.TrimEnd('\\', '/');
        if (!string.IsNullOrEmpty(name) && trimmed.EndsWith(name, StringComparison.OrdinalIgnoreCase))
        {
            var offset = trimmed.Length - name.Length;
            _folderDetails.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = trimmed[..offset] });
            _folderDetails.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run
            {
                Text = trimmed[offset..],
                Foreground = Brush(255, 255, 255)
            });
            _folderDetails.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = _folderDetailsText[trimmed.Length..] });
        }
        else _folderDetails.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = _folderDetailsText });
        ToolTipService.SetToolTip(_folderDetails, _folderDetailsText);
    }

    public void SetZoomLevel(double zoomLevel)
    {
        _zoomLevel = zoomLevel;
        _imageToolbar?.SetZoom(zoomLevel);
    }

    public void SetFileMetadata(string metadata)
    {
        _fileMetadataText = metadata;
        if (_fileMetadata is null) return;
        _fileMetadata.Text = metadata;
        _fileMetadata.Visibility = string.IsNullOrEmpty(metadata)
            ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
        ToolTipService.SetToolTip(_fileMetadata, metadata);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_fileMetadata, metadata);
    }

    public void SetViewerCapabilities(MediFiler3.Application.Viewing.ViewerCapabilities capabilities, bool canInteract)
    {
        _imageControlsVisible = capabilities.HasFlag(MediFiler3.Application.Viewing.ViewerCapabilities.Zoom);
        _imageControlsEnabled = _imageControlsVisible && canInteract;
        _textControlsVisible = capabilities.HasFlag(MediFiler3.Application.Viewing.ViewerCapabilities.DocumentScroll);
        _mediaControlsVisible = capabilities.HasFlag(MediFiler3.Application.Viewing.ViewerCapabilities.Playback);
        if (_mediaToolbar is not null) _mediaToolbar.Control.Visibility = _mediaControlsVisible
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (_textToolbar is not null)
            _textToolbar.Control.Visibility = _textControlsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        if (_imageToolbar is not null)
        {
            _imageToolbar.Control.Visibility = _imageControlsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
            _imageToolbar.Control.IsEnabled = _imageControlsEnabled;
        }
    }

    public void SetTextViewerState(bool scrolling, bool canPrevious, bool canNext)
    {
        _textScrolling = scrolling;
        _canPreviousText = canPrevious;
        _canNextText = canNext;
        _textToolbar?.SetState(scrolling, canPrevious, canNext);
    }

    public void SetExternalOpenState(MediFiler3.Application.Files.ExternalOpenState state)
    {
        _externalState = state;
        _externalToolbar?.SetState(state);

    }

    public void SetMediaPlaybackState(MediFiler3.Application.Viewing.MediaPlaybackState state)
    {
        _mediaState = state;
        _mediaToolbar?.SetState(state);
    }

    protected override HandleRef BuildWindowCore(HandleRef hwndParent)
    {
        _xamlSource = new DesktopWindowXamlSource();
        _xamlSource.Initialize(Win32Interop.GetWindowIdFromWindow(hwndParent.Handle));
        _xamlSource.ShouldConstrainPopupsToWorkArea = true;
        _xamlSource.Content = CreateToolbarLayout();

        var islandHandle = Win32Interop.GetWindowFromWindowId(_xamlSource.SiteBridge.WindowId);
        return new HandleRef(this, islandHandle);
    }

    protected override void DestroyWindowCore(HandleRef hwnd)
    {
        if (_xamlSource is not null)
        {
            _xamlSource.Content = null;
            _xamlSource.Dispose();
            _xamlSource = null;
        }

        _folderDetails = null;
        _fileMetadata = null;
        _imageToolbar = null;
        _textToolbar = null;
        _mediaToolbar = null;
        _externalToolbar = null;
        _renameFileButton = null;
        _deleteFileButton = null;
        _rootElement = null;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var arrangedSize = base.ArrangeOverride(finalSize);
        if (_xamlSource is not null)
        {
            var dpi = VisualTreeHelper.GetDpi(this);
            var width = Math.Max(0, (int)Math.Round(arrangedSize.Width * dpi.DpiScaleX));
            var height = Math.Max(0, (int)Math.Round(arrangedSize.Height * dpi.DpiScaleY));
            _xamlSource.SiteBridge.MoveAndResize(new RectInt32(0, 0, width, height));
        }

        return arrangedSize;
    }

    protected override bool TabIntoCore(TraversalRequest request)
    {
        if (_xamlSource is null)
        {
            return false;
        }

        var reason = request.FocusNavigationDirection == FocusNavigationDirection.Last
            ? XamlSourceFocusNavigationReason.Last
            : XamlSourceFocusNavigationReason.First;
        return _xamlSource.NavigateFocus(new XamlSourceFocusNavigationRequest(reason)).WasFocusMoved;
    }

    private Grid CreateToolbarLayout()
    {
        var background = Brush(0, 0, 0);
        var layout = new Grid { Background = background, RequestedTheme = ElementTheme.Dark };
        _rootElement = layout;
        for (var index = 0; index < 3; index++)
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var left = CreateFolderContext();
        left.HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        Grid.SetColumn(left, 0);
        layout.Children.Add(left);

        var middle = CreateCommandBar(Microsoft.UI.Xaml.HorizontalAlignment.Center, new Thickness(8, 0, 8, 0));
        _renameFileButton = CreateButton(Symbol.Edit, "Rename current file", () => RenameFileRequested?.Invoke());
        _deleteFileButton = CreateButton(Symbol.Delete, "Send current file to Recycle Bin", () => DeleteFileRequested?.Invoke());
        _undoButton = CreateButton(Symbol.Undo, "Undo last file operation", () => UndoRequested?.Invoke());
        _redoButton = CreateButton(Symbol.Redo, "Redo last file operation", () => RedoRequested?.Invoke());
        _renameFileButton.IsEnabled = _fileActionsEnabled;
        _deleteFileButton.IsEnabled = _fileActionsEnabled;
        middle.PrimaryCommands.Add(CreateButton(Symbol.Add, "Add folders", () => LoadFoldersRequested?.Invoke()));
        middle.PrimaryCommands.Add(new AppBarSeparator());
        middle.PrimaryCommands.Add(_renameFileButton);
        middle.PrimaryCommands.Add(_deleteFileButton);
        middle.PrimaryCommands.Add(new AppBarSeparator());
        middle.PrimaryCommands.Add(_undoButton);
        middle.PrimaryCommands.Add(_redoButton);
        middle.PrimaryCommands.Add(new AppBarSeparator());
        _externalToolbar = new ExternalOpenToolbarGroup(() => OpenDefaultRequested?.Invoke(), () => OpenWithRequested?.Invoke());
        middle.PrimaryCommands.Add(_externalToolbar.OpenButton);
        middle.PrimaryCommands.Add(_externalToolbar.ChooseButton);
        Grid.SetColumn(middle, 1);
        layout.Children.Add(middle);

        _imageToolbar = new ImageToolbarGroup(() => ZoomOutRequested?.Invoke(),
            () => ResetZoomRequested?.Invoke(), () => ZoomInRequested?.Invoke());
        _imageToolbar.SetZoom(_zoomLevel);
        _imageToolbar.Control.Margin = new Thickness(8, 0, 8, 0);
        _imageToolbar.Control.Visibility = _imageControlsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        _imageToolbar.Control.IsEnabled = _imageControlsEnabled;
        Grid.SetColumn(_imageToolbar.Control, 2);
        layout.Children.Add(_imageToolbar.Control);
        _textToolbar = new TextToolbarGroup(() => ToggleTextInteractionRequested?.Invoke(),
            () => PreviousTextWindowRequested?.Invoke(), () => NextTextWindowRequested?.Invoke());
        _textToolbar.Control.Margin = new Thickness(8, 0, 8, 0);
        _textToolbar.Control.Visibility = _textControlsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        _textToolbar.SetState(_textScrolling, _canPreviousText, _canNextText);
        Grid.SetColumn(_textToolbar.Control, 2);
        layout.Children.Add(_textToolbar.Control);
        _mediaToolbar = new MediaToolbarGroup(() => TogglePlaybackRequested?.Invoke(), () => StopPlaybackRequested?.Invoke(),
            seconds => SeekPlaybackRequested?.Invoke(seconds), volume => PlaybackVolumeRequested?.Invoke(volume),
            () => TogglePlaybackMuteRequested?.Invoke(), seconds => JumpPlaybackRequested?.Invoke(seconds));
        _mediaToolbar.Control.Margin = new Thickness(8, 0, 8, 0);
        _mediaToolbar.Control.Visibility = _mediaControlsVisible ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
        _mediaToolbar.SetState(_mediaState);
        Grid.SetColumn(_mediaToolbar.Control, 2);
        layout.Children.Add(_mediaToolbar.Control);
        SetExternalOpenState(_externalState);

        return layout;
    }

    private StackPanel CreateFolderContext()
    {
        var panel = new StackPanel
        {
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Spacing = 0,
            Margin = new Thickness(12, 0, 4, 0)
        };
        _folderDetails = new TextBlock
        {
            Text = _folderDetailsText,
            FontSize = 10,
            Foreground = Brush(163, 166, 174),
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
        };
        _fileMetadata = new TextBlock
        {
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(245, 245, 245),
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            TextWrapping = Microsoft.UI.Xaml.TextWrapping.NoWrap
        };
        SetFileMetadata(_fileMetadataText);
        panel.Children.Add(_fileMetadata);
        panel.Children.Add(_folderDetails);
        UpdateFolderPath();
        return panel;
    }

    private static CommandBar CreateCommandBar(Microsoft.UI.Xaml.HorizontalAlignment alignment, Thickness margin)
    {
        return new CommandBar
        {
            IsOpen = false,
            Height = 48,
            Margin = margin,
            Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(ColorHelper.FromArgb(0, 0, 0, 0)),
            Foreground = Brush(245, 245, 245),
            RequestedTheme = ElementTheme.Dark,
            IsDynamicOverflowEnabled = false,
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed,
            OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
            HorizontalAlignment = alignment,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Top
        };
    }

    private static AppBarButton CreateButton(Symbol symbol, string accessibleName, Action onClick)
    {
        var button = new AppBarButton
        {
            Icon = new SymbolIcon(symbol),
            Label = accessibleName,
            LabelPosition = CommandBarLabelPosition.Collapsed,
            Width = 32
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, new ToolTip { Content = accessibleName });
        button.Click += (_, _) => onClick();
        return button;
    }

    private void AddFolderMenuItem(MenuFlyout menu, string label, string action, string folderName,
        Func<string, Task> actionHandler)
    {
        var item = new MenuFlyoutItem { Text = label };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(item, $"{label}: {folderName}");
        item.Click += async (_, _) => await actionHandler(action);
        menu.Items.Add(item);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(255, red, green, blue));
}
