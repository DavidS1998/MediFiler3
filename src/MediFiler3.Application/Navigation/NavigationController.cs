using MediFiler3.Application.Folders;
using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Navigation;

public sealed class NavigationController(NavigationSessionState navigationState, Func<FolderTreeEntry, Task> activateFolder)
{
    private readonly NavigationSessionState _navigationState = navigationState;
    private readonly Func<FolderTreeEntry, Task> _activateFolder = activateFolder;
    private int _overscrollDirection;
    private int _overscrollCount;
    private bool _changingFolderFromOverscroll;
    private long _contextVersion;
    private const int OverscrollAttemptsToChangeFolder = 5;
    public event Action<FileEntry>? FileSelected;
    public event Action<string>? StatusChanged;

    public void SelectFile(FileEntry file)
    {
        if (!_navigationState.Files.Contains(file)) return;
        _contextVersion++;
        _navigationState.SetCurrentFile(file);
        ResetBoundaryAttempts();
        FileSelected?.Invoke(file);
    }

    public void CancelPendingNavigation()
    {
        _contextVersion++;
        ResetBoundaryAttempts();
    }

    public void NavigateByOneFile(int direction)
    {
        if (_navigationState.Files.Count == 0)
        {
            RegisterBoundaryAttempt(direction);
            return;
        }

        var currentIndex = _navigationState.CurrentFile is null
            ? -1
            : _navigationState.Files.IndexOf(_navigationState.CurrentFile);
        var nextIndex = currentIndex + direction;
        if (nextIndex >= 0 && nextIndex < _navigationState.Files.Count)
        {
            ResetBoundaryAttempts();
            SelectFile(_navigationState.Files[nextIndex]);
            return;
        }

        RegisterBoundaryAttempt(direction);
    }

    private void RegisterBoundaryAttempt(int direction)
    {
        if (_overscrollDirection != direction)
        {
            _overscrollDirection = direction;
            _overscrollCount = 0;
        }

        _overscrollCount++;
        if (_overscrollCount < OverscrollAttemptsToChangeFolder || _changingFolderFromOverscroll)
        {
            return;
        }

        _overscrollCount = 0;
        _changingFolderFromOverscroll = true;
        _ = ActivateAdjacentFolderWithFilesAsync(direction);
    }

    private async Task ActivateAdjacentFolderWithFilesAsync(int direction)
    {
        var version = _contextVersion;
        try
        {
            var originalFolder = _navigationState.ActiveFolder;
            var folders = EnumerateFoldersInTreeOrder(_navigationState.Roots).ToArray();
            var currentIndex = Array.FindIndex(folders, folder => ReferenceEquals(folder, _navigationState.ActiveFolder));
            for (var index = currentIndex + direction; index >= 0 && index < folders.Length; index += direction)
            {
                var folder = folders[index];
                await _activateFolder(folder);
                if (version != _contextVersion) return;
                folder.IsSelected = true;
                if (_navigationState.Files.Count > 0)
                {
                    ResetBoundaryAttempts();
                    return;
                }
            }

            if (originalFolder is not null)
            {
                await _activateFolder(originalFolder);
                originalFolder.IsSelected = true;
            }

            StatusChanged?.Invoke(direction > 0 ? "No files in later folders." : "No files in earlier folders.");
        }
        finally
        {
            _changingFolderFromOverscroll = false;
        }
    }

    public void ResetBoundaryAttempts()
    {
        _overscrollDirection = 0;
        _overscrollCount = 0;
    }

    private static IEnumerable<FolderTreeEntry> EnumerateFoldersInTreeOrder(IEnumerable<FolderTreeEntry> folders)
    {
        foreach (var folder in folders)
        {
            yield return folder;
            foreach (var child in EnumerateFoldersInTreeOrder(folder.Children))
            {
                yield return child;
            }
        }
    }

}
