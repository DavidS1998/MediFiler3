using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class NavigationIndexTests
{
    [Fact]
    public void LargeFolderNavigationKeepsPositionAndSelectionWithoutLosingBoundaryBehavior()
    {
        var state = new NavigationSessionState();
        var files = Enumerable.Range(0, 100000).Select(i => new FileEntry($"C:/files/{i}.txt", $"{i}.txt", 1, DateTime.UnixEpoch)).ToArray();
        state.ReplaceFiles(files);
        var navigation = new NavigationController(state, _ => Task.CompletedTask);
        navigation.SelectFile(files[50000]);
        for (var i = 0; i < 1000; i++) navigation.NavigateByOneFile(1);
        Assert.Same(files[51000], state.CurrentFile);
        Assert.Equal(51001, state.CurrentFilePosition);
        state.Files.RemoveAt(0);
        Assert.Equal(51000, state.CurrentFilePosition);
        // Preserve record-equality membership semantics when callers supply an equivalent entry.
        navigation.SelectFile(files[100] with { });
        Assert.Equal(100, state.CurrentFilePosition);
        state.ReplaceFiles([files[0]]);
        Assert.False(state.ContainsFile(files[51000]));
        Assert.Equal(1, state.CurrentFilePosition);
    }
}
