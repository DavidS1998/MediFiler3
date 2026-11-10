using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using MediFiler3.Application.Folders;
using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Navigation;

public sealed class NavigationSessionState : INotifyPropertyChanged
{
    private FolderTreeEntry? _activeFolder;
    private FileEntry? _currentFile;
    private readonly Dictionary<FileEntry, int> _fileIndices = [];
    private bool _replacingFiles;
    public NavigationSessionState() => Files.CollectionChanged += (_, _) =>
    {
        if (_replacingFiles) return;
        ReindexFiles();
        OnPropertyChanged(nameof(CurrentFilePosition));
    };
    private void ReindexFiles()
    {
        _fileIndices.Clear();
        for (var i = 0; i < Files.Count; i++) _fileIndices.TryAdd(Files[i], i);
    }
    public int IndexOfFile(FileEntry file) => _fileIndices.TryGetValue(file, out var index) ? index : -1;
    public bool ContainsFile(FileEntry file) => _fileIndices.ContainsKey(file);

    public ObservableCollection<FolderTreeEntry> Roots { get; } = [];
    public ObservableCollection<FileEntry> Files { get; } = [];
    public FolderTreeEntry? ActiveRoot => GetRootForFolder(ActiveFolder);
    public FolderTreeEntry? GetRootForFolder(FolderTreeEntry? folder)
    {
        if (folder is null) return null;
        while (folder.Parent is not null) folder = folder.Parent;
        return Roots.Contains(folder) ? folder : null;
    }

    public FolderTreeEntry? ActiveFolder
    {
        get => _activeFolder;
        private set
        {
            var previousRoot = ActiveRoot;
            if (SetProperty(ref _activeFolder, value) && !ReferenceEquals(previousRoot, ActiveRoot))
                OnPropertyChanged(nameof(ActiveRoot));
        }
    }

    public FileEntry? CurrentFile
    {
        get => _currentFile;
        private set
        {
            if (SetProperty(ref _currentFile, value))
            {
                OnPropertyChanged(nameof(CurrentFilePosition));
            }
        }
    }

    public int CurrentFilePosition => CurrentFile is null ? 0 : IndexOfFile(CurrentFile) + 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReplaceRoots(IEnumerable<FolderTreeEntry> roots, bool preserveActiveContext = false)
    {
        Roots.Clear();
        foreach (var root in roots)
        {
            Roots.Add(root);
        }

        OnPropertyChanged(nameof(ActiveRoot));
        if (!preserveActiveContext)
        {
            ActiveFolder = null;
            ReplaceFiles([]);
        }
    }

    public void SetActiveFolder(FolderTreeEntry? folder)
    {
        if (ReferenceEquals(ActiveFolder, folder))
        {
            return;
        }

        if (ActiveFolder is not null)
        {
            ActiveFolder.IsSelected = false;
        }

        ActiveFolder = folder;
        if (folder is not null)
        {
            folder.IsSelected = true;
        }

        ReplaceFiles([]);
    }

    public void ReplaceFiles(IEnumerable<FileEntry> files)
    {
        _replacingFiles = true;
        try
        {
            Files.Clear();
            foreach (var file in files) Files.Add(file);
        }
        finally { _replacingFiles = false; ReindexFiles(); }

        OnPropertyChanged(nameof(Files));
        SetCurrentFile(Files.FirstOrDefault());
        OnPropertyChanged(nameof(CurrentFilePosition));
    }

    public void SetCurrentFile(FileEntry? file)
    {
        if (file is not null && !ContainsFile(file))
        {
            return;
        }

        CurrentFile = file;
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
