using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.Windows;
using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

public partial class BrowseView : UserControl
{
    private readonly IFolderTreeService _folderTreeService;
    private CancellationTokenSource? _loadCancellation;
    public ObservableCollection<FolderTreeEntry> Roots { get; } = [];
    public event Action<string>? StatusChanged;

    public BrowseView(IFolderTreeService folderTreeService)
    {
        _folderTreeService = folderTreeService;
        InitializeComponent();
        DataContext = this;
    }

    public async Task LoadRootsAsync(IEnumerable<string> paths)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        Roots.Clear();
        var requestedRoots = paths.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (requestedRoots.Length == 0)
        {
            StatusChanged?.Invoke("No folders selected.");
            return;
        }

        StatusChanged?.Invoke("Loading folder hierarchy...");
        var result = await _folderTreeService.LoadRootsAsync(requestedRoots, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var root in result.Roots)
        {
            Roots.Add(root);
        }

        if (result.Errors.Count > 0)
        {
            StatusChanged?.Invoke($"Loaded {Roots.Count} root(s); {result.Errors.Count} folder(s) could not be accessed.");
            MessageBox.Show(string.Join(Environment.NewLine, result.Errors), "Some folders could not be loaded", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        else
        {
            StatusChanged?.Invoke($"Loaded {Roots.Count} folder root(s).");
        }
    }
}
