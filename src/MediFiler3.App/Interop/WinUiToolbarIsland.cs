using Microsoft.UI;
using Microsoft.UI.Content;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Windows.Graphics;
using Thickness = Microsoft.UI.Xaml.Thickness;
using GridLength = Microsoft.UI.Xaml.GridLength;
using GridUnitType = Microsoft.UI.Xaml.GridUnitType;

namespace MediFiler3.Interop;

public sealed class WinUiToolbarIsland : HwndHost
{
    private DesktopWindowXamlSource? _xamlSource;
    private TextBlock? _folderTitle;
    private TextBlock? _folderDetails;
    private TextBlock? _zoomText;
    private string _folderTitleText = "MediFiler3";
    private string _folderDetailsText = "No folder loaded";
    private double _zoomLevel = 1d;

    public event Action? LoadFoldersRequested;
    public event Action? ResetZoomRequested;
    public event Action? ZoomInRequested;
    public event Action? ZoomOutRequested;

    public void SetFolderContext(string title, string details)
    {
        _folderTitleText = title;
        _folderDetailsText = details;
        if (_folderTitle is not null)
        {
            _folderTitle.Text = title;
            _folderDetails!.Text = details;
        }
    }

    public void SetZoomLevel(double zoomLevel)
    {
        _zoomLevel = zoomLevel;
        if (_zoomText is not null)
        {
            _zoomText.Text = $"{zoomLevel:P0}";
        }
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

        _folderTitle = null;
        _folderDetails = null;
        _zoomText = null;
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
        var background = Brush(14, 15, 18);
        var layout = new Grid { Background = background };
        for (var index = 0; index < 3; index++)
        {
            layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        var left = CreateCommandBar(CreateFolderContext());
        layout.Children.Add(left);

        var loadButton = CreateButton(Symbol.Add, "Load folders", () => LoadFoldersRequested?.Invoke());
        var center = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Center,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Spacing = 4
        };
        center.Children.Add(loadButton);
        var middle = CreateCommandBar(center);
        Grid.SetColumn(middle, 1);
        layout.Children.Add(middle);

        var zoomControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Right,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Spacing = 2
        };
        zoomControls.Children.Add(CreateButton(Symbol.ZoomOut, "Zoom out", () => ZoomOutRequested?.Invoke()));
        zoomControls.Children.Add(CreateButton(Symbol.Zoom, "Reset zoom to fit", () => ResetZoomRequested?.Invoke()));
        _zoomText = new TextBlock
        {
            Text = $"{_zoomLevel:P0}",
            FontSize = 12,
            Foreground = Brush(225, 227, 231),
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center,
            Margin = new Thickness(2, 0, 0, 0)
        };
        zoomControls.Children.Add(_zoomText);
        zoomControls.Children.Add(CreateButton(Symbol.ZoomIn, "Zoom in", () => ZoomInRequested?.Invoke()));
        var right = CreateCommandBar(zoomControls);
        Grid.SetColumn(right, 2);
        layout.Children.Add(right);

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
        _folderTitle = new TextBlock
        {
            Text = _folderTitleText,
            FontSize = 13,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Foreground = Brush(245, 245, 245),
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
        };
        _folderDetails = new TextBlock
        {
            Text = _folderDetailsText,
            FontSize = 10,
            Foreground = Brush(163, 166, 174),
            TextTrimming = Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis
        };
        panel.Children.Add(_folderTitle);
        panel.Children.Add(_folderDetails);
        return panel;
    }

    private static CommandBar CreateCommandBar(Microsoft.UI.Xaml.UIElement content)
    {
        return new CommandBar
        {
            Content = content,
            Height = 48,
            Background = Brush(14, 15, 18),
            Foreground = Brush(245, 245, 245),
            RequestedTheme = ElementTheme.Dark,
            IsDynamicOverflowEnabled = false,
            DefaultLabelPosition = CommandBarDefaultLabelPosition.Collapsed,
            OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Center
        };
    }

    private static AppBarButton CreateButton(Symbol symbol, string accessibleName, Action onClick)
    {
        var button = new AppBarButton
        {
            Icon = new SymbolIcon(symbol),
            Label = accessibleName,
            LabelPosition = CommandBarLabelPosition.Collapsed,
            Width = 40,
            Height = 40,
            MinWidth = 40,
            MinHeight = 40,
            Padding = new Thickness(0)
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, accessibleName);
        ToolTipService.SetToolTip(button, new ToolTip { Content = accessibleName });
        button.Click += (_, _) => onClick();
        return button;
    }

    private static Microsoft.UI.Xaml.Media.SolidColorBrush Brush(byte red, byte green, byte blue) =>
        new(ColorHelper.FromArgb(255, red, green, blue));
}
