using System.Windows.Controls;
using System.Windows;
using System.IO;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Views;

public partial class BrowseView : UserControl
{
    private readonly IFolderTreeService _folderTreeService;
    private readonly IFileListingService _fileListingService;
    private readonly NavigationSessionState _navigationState;
    private CancellationTokenSource? _loadCancellation;
    private CancellationTokenSource? _fileLoadCancellation;
    public event Action<string>? StatusChanged;

    public BrowseView(IFolderTreeService folderTreeService, IFileListingService fileListingService, NavigationSessionState navigationState)
    {
        _folderTreeService = folderTreeService;
        _fileListingService = fileListingService;
        _navigationState = navigationState;
        InitializeComponent();
        DataContext = navigationState;
    }

    public async Task LoadRootsAsync(IEnumerable<string> paths)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        _fileLoadCancellation?.Cancel();
        _navigationState.ReplaceRoots([]);
        var requestedRoots = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedRoots.Length == 0)
        {
            StatusChanged?.Invoke("No folders selected.");
            return;
        }

        StatusChanged?.Invoke("Loading folder hierarchy...");
        try
        {
            var result = await _folderTreeService.LoadRootsAsync(requestedRoots, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            _navigationState.ReplaceRoots(result.Roots);

            if (result.Errors.Count > 0)
            {
                StatusChanged?.Invoke($"Loaded {result.Roots.Count} root(s); {result.Errors.Count} folder(s) could not be accessed.");
                MessageBox.Show(string.Join(Environment.NewLine, result.Errors), "Some folders could not be loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                StatusChanged?.Invoke($"Loaded {result.Roots.Count} folder root(s).");
            }

            if (result.Roots.FirstOrDefault() is { } firstRoot)
            {
                await ActivateFolderAsync(firstRoot);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer context replaced this load.
        }
    }

    private async void OnFolderSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderTreeEntry folder)
        {
            await ActivateFolderAsync(folder);
        }
    }

    private async Task ActivateFolderAsync(FolderTreeEntry folder)
    {
        if (ReferenceEquals(_navigationState.ActiveFolder, folder))
        {
            return;
        }

        _fileLoadCancellation?.Cancel();
        _fileLoadCancellation?.Dispose();
        _fileLoadCancellation = new CancellationTokenSource();
        var cancellationToken = _fileLoadCancellation.Token;
        _navigationState.SetActiveFolder(folder);
        StatusChanged?.Invoke($"Loading files in {folder.Name}...");

        try
        {
            var result = await _fileListingService.GetFilesAsync(folder.Path, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!ReferenceEquals(_navigationState.ActiveFolder, folder))
            {
                return;
            }

            _navigationState.ReplaceFiles(result.Files);
            FileList.SelectedItem = _navigationState.CurrentFile;
            EmptyFilesNotice.Visibility = result.Files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusChanged?.Invoke(result.Errors.Count == 0
                ? $"{result.Files.Count} file(s) in {folder.Name}."
                : $"Loaded {result.Files.Count} file(s); {result.Errors.Count} item(s) could not be read.");
            if (result.Errors.Count > 0)
            {
                var errorSample = result.Errors.Take(5).ToList();
                if (result.Errors.Count > errorSample.Count)
                {
                    errorSample.Add($"...and {result.Errors.Count - errorSample.Count} more.");
                }

                MessageBox.Show(string.Join(Environment.NewLine, errorSample), "Some files could not be read", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A newer folder selection replaced this listing.
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (ReferenceEquals(_navigationState.ActiveFolder, folder))
            {
                _navigationState.ReplaceFiles([]);
                EmptyFilesNotice.Visibility = Visibility.Visible;
                StatusChanged?.Invoke($"Could not read files in {folder.Name}.");
                MessageBox.Show(exception.Message, "Folder could not be read", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnFileSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _navigationState.SetCurrentFile(FileList.SelectedItem as FileEntry);
    }
}
