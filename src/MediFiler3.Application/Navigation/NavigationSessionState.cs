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

    public ObservableCollection<FolderTreeEntry> Roots { get; } = [];
    public ObservableCollection<FileEntry> Files { get; } = [];
    public FolderTreeEntry? ActiveFolder
    {
        get => _activeFolder;
        private set => SetProperty(ref _activeFolder, value);
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

    public int CurrentFilePosition => CurrentFile is null ? 0 : Files.IndexOf(CurrentFile) + 1;

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ReplaceRoots(IEnumerable<FolderTreeEntry> roots, bool preserveActiveContext = false)
    {
        Roots.Clear();
        foreach (var root in roots)
        {
            Roots.Add(root);
        }

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
        Files.Clear();
        foreach (var file in files)
        {
            Files.Add(file);
        }

        OnPropertyChanged(nameof(Files));
        SetCurrentFile(Files.FirstOrDefault());
        OnPropertyChanged(nameof(CurrentFilePosition));
    }

    public void SetCurrentFile(FileEntry? file)
    {
        if (file is not null && !Files.Contains(file))
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
