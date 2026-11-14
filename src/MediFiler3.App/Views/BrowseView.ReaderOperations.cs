using MediFiler3.Application.Folders;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private CancellationTokenSource? _readerCommandCancellation = null;

    // Retain the internal entry point for callers, but Reader cannot mutate files.
    private Task RunReaderFileCommandAsync(FolderTreeEntry? destination) => Task.CompletedTask;
}
