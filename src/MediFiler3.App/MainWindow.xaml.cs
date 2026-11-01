using MediFiler3.Interop;
using MediFiler3.Views;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using Microsoft.Win32;
using MediFiler3.Application.Folders;
using System.IO;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3;

public partial class MainWindow : Window
{
    private const uint DwmwaCaptionColor = 35;
    private const uint DwmwaTextColor = 36;
    private const double CompactSidebarWidth = 56;

    public static readonly DependencyProperty TitleBarBackgroundProperty = DependencyProperty.Register(
        nameof(TitleBarBackground),
        typeof(Brush),
        typeof(MainWindow),
        new FrameworkPropertyMetadata(Brushes.Black, OnCaptionColorPropertyChanged));

    public static readonly DependencyProperty TitleBarForegroundProperty = DependencyProperty.Register(
        nameof(TitleBarForeground),
        typeof(Brush),
        typeof(MainWindow),
        new FrameworkPropertyMetadata(Brushes.White, OnCaptionColorPropertyChanged));

    public static readonly DependencyProperty IsSidebarVisibleProperty = DependencyProperty.Register(
        nameof(IsSidebarVisible),
        typeof(bool),
        typeof(MainWindow),
        new FrameworkPropertyMetadata(true, OnSidebarLayoutPropertyChanged));

    private readonly HomeView _homeView = new();
    private readonly BrowseView _browseView;
    private readonly ILogger<MainWindow> _logger;
    private readonly NavigationSessionState _navigationState;
    private bool _sourceInitialized;

    public Brush TitleBarBackground
    {
        get => (Brush)GetValue(TitleBarBackgroundProperty);
        set => SetValue(TitleBarBackgroundProperty, value);
    }

    public Brush TitleBarForeground
    {
        get => (Brush)GetValue(TitleBarForegroundProperty);
        set => SetValue(TitleBarForegroundProperty, value);
    }

    public bool IsSidebarVisible
    {
        get => (bool)GetValue(IsSidebarVisibleProperty);
        set => SetValue(IsSidebarVisibleProperty, value);
    }

    public MainWindow(ILogger<MainWindow> logger, IFolderTreeService folderTreeService, IFileListingService fileListingService, NavigationSessionState navigationState)
    {
        _logger = logger;
        _navigationState = navigationState;
        _browseView = new BrowseView(folderTreeService, fileListingService, navigationState);
        InitializeComponent();
        PageContent.Content = _homeView;
        NavigationIsland.DestinationSelected += OnNavigationSelectionChanged;
        _browseView.StatusChanged += status => LoadStatus.Text = status;
        _navigationState.PropertyChanged += OnNavigationStateChanged;
        UpdateWindowTitle();
        SourceInitialized += OnWindowSourceInitialized;
        ApplySidebarLayout();
        logger.LogInformation("Main window initialized.");
    }

    private static void OnCaptionColorPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MainWindow window && window._sourceInitialized)
        {
            window.ApplyCaptionColors();
        }
    }

    private static void OnSidebarLayoutPropertyChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is MainWindow window && window.IsInitialized)
        {
            window.ApplySidebarLayout();
        }
    }

    private void OnWindowSourceInitialized(object? sender, EventArgs e)
    {
        _sourceInitialized = true;
        ApplyCaptionColors();
    }

    private void ApplyCaptionColors()
    {
        var windowHandle = new WindowInteropHelper(this).Handle;
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        ApplyCaptionColor(windowHandle, DwmwaCaptionColor, TitleBarBackground, nameof(TitleBarBackground));
        ApplyCaptionColor(windowHandle, DwmwaTextColor, TitleBarForeground, nameof(TitleBarForeground));
    }

    private void ApplyCaptionColor(IntPtr windowHandle, uint attribute, Brush brush, string propertyName)
    {
        if (brush is not SolidColorBrush solidBrush)
        {
            _logger.LogWarning("{PropertyName} must be a solid color brush for the native title bar.", propertyName);
            return;
        }

        var color = solidBrush.Color;
        var colorRef = (uint)(color.R | (color.G << 8) | (color.B << 16));
        var result = DwmSetWindowAttribute(windowHandle, attribute, ref colorRef, sizeof(uint));
        if (result < 0)
        {
            _logger.LogWarning("Windows could not apply {PropertyName}; DwmSetWindowAttribute returned 0x{HResult:X8}.", propertyName, result);
        }
    }

    private void ApplySidebarLayout()
    {
        if (!IsInitialized)
        {
            return;
        }

        Sidebar.Visibility = IsSidebarVisible ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = IsSidebarVisible
            ? new GridLength(CompactSidebarWidth)
            : new GridLength(0);
    }

    private void OnNavigationSelectionChanged(string destination)
    {
        PageContent.Content = destination switch
        {
            "Browse" => _browseView,
            _ => _homeView
        };
    }

    private void OnNavigationStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(NavigationSessionState.CurrentFilePosition) or nameof(NavigationSessionState.Files))
        {
            UpdateWindowTitle();
        }
    }

    private void UpdateWindowTitle()
    {
        var currentFile = _navigationState.CurrentFile;
        Title = currentFile is null || _navigationState.CurrentFilePosition == 0
            ? "MediFiler3"
            : $"({_navigationState.CurrentFilePosition}/{_navigationState.Files.Count}) {currentFile.Name}";
    }

    private async void OnLoadFoldersClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Load folders",
            Multiselect = true
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await LoadFolderPathsAsync(dialog.FolderNames);
    }

    private void OnWindowDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0)
        {
            return;
        }

        var folders = paths.Where(Directory.Exists).ToArray();
        var files = paths.Length - folders.Length;
        if (files > 0)
        {
            LoadStatus.Text = $"Ignored {files} dropped file(s); drop folders to load a collection.";
        }

        if (folders.Length > 0)
        {
            await LoadFolderPathsAsync(folders);
        }
    }

    private async Task LoadFolderPathsAsync(IEnumerable<string> folderPaths)
    {
        PageContent.Content = _browseView;
        NavigationIsland.SelectDestination("Browse");
        LoadStatus.Text = "Loading folder hierarchy…";
        try
        {
            await _browseView.LoadRootsAsync(folderPaths);
        }
        catch (OperationCanceledException)
        {
            // A subsequent folder load replaced this one.
        }
    }

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, uint attribute, ref uint value, uint valueSize);
}
