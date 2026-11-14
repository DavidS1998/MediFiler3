using System.IO;
using System.Reflection;
using MediFiler3.Application.Files;
using MediFiler3.Application.Folders;
using MediFiler3.Application.Navigation;
using MediFiler3.Application.Operations;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunReaderMutationChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, WinUiNavigationIsland navigation, WinUiToolbarIsland toolbar, ManualMonitor monitor)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Root, "reader-mutations")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(directory, "target")).FullName;
        var pixels = new byte[64 * 320 * 4];
        for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 150; pixels[i + 3] = 255; }
        foreach (var name in new[] { "a.png", "b.png", "c.png", "d.png" })
        {
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(System.Windows.Media.Imaging.BitmapSource.Create(
                64, 320, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, 64 * 4)));
            using var output = File.Create(Path.Combine(directory, name)); encoder.Save(output);
        }
        File.WriteAllText(Path.Combine(directory, "b.txt"), "unsupported shared neighbor");
        await LoadRestartRootsAsync(window, [directory]);
        await WaitUntil(() => state.Files.Count == 5 && state.Roots[0].Children.Any(child => child.Path == target));
        navigation.SelectDestination("Reader"); browse.SetReaderOptions(new());
        WinUiReaderIsland Reader() => (WinUiReaderIsland)Field(browse, "_reader")!;
        async Task Jump(string name)
        {
            await WaitUntil(() => state.Reader.Pages.Any(file => file.Name == name));
            Require(browse.JumpReaderPage(state.Reader.Pages.ToList().FindIndex(file => file.Name == name)), "Jump failed: " + name);
            await WaitUntil(() => state.CurrentFile?.Name == name && !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
        }
        async Task Settled(string name)
        {
            try { await WaitUntil(() => state.CurrentFile?.Name == name && state.Reader.Pages.Any(file => file.Name == name) &&
                !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!); }
            catch (TimeoutException exception) { throw new InvalidOperationException($"Expected {name}; current={state.CurrentFile?.Name}; page={browse.ReaderPageIndex}; anchor={state.Reader.Anchor}; restore={Field(Reader(), "_restore")}; queued={Field(Reader(), "_queued")}; depth={Field(browse, "_readerReconciliationDepth")}; prefer={Field(browse, "_readerPreferCurrent")}; notice={((System.Windows.Controls.TextBlock)browse.FindName("OperationNoticeText")).Text}; files={string.Join(",", state.Files.Select(file => file.Name))}", exception); }
        }
        object? Source(string name) => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>()
            .Where(row => ((MediFiler3.Domain.Files.FileEntry)row.GetType().GetProperty("File")!.GetValue(row)!).Name == name)
            .Select(row => ((Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!).Source).FirstOrDefault();
        async Task Move() => await (Task)typeof(BrowseView).GetMethod("MoveCurrentFileToFolderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(browse, [state.Roots[0].Children.Single(child => child.Path == target)])!;
        var originalDialogs = browse.DialogService; var dialogs = new CollisionDialogs(window.Dispatcher); browse.DialogService = dialogs;
        try
        {
            await Check("Reader rejects file moves rename recycle Undo and Redo while folder commands remain available", async () =>
            {
                await Jump("b.png");
                navigation.SelectDestination("Browse");
                dialogs.Name = "b-history";
                await browse.PromptRenameCurrentFileAsync();
                navigation.SelectDestination("Reader"); await Jump("b-history.png");
                await browse.UndoLastOperationAsync();
                Require(File.Exists(Path.Combine(directory, "b-history.png")), "Reader replayed Undo");
                navigation.SelectDestination("Browse"); await browse.UndoLastOperationAsync();
                navigation.SelectDestination("Reader"); await Jump("b.png");
                var snapshot = state.Files.ToDictionary(file => file.FullPath, file => File.ReadAllBytes(file.FullPath));
                var prompts = 0;
                browse.DialogService = new ReaderScopedDialogs { OnPrompt = () => { prompts++; return "forbidden"; } };
                try
                {
                    await Move();
                    await browse.PromptRenameCurrentFileAsync();
                    await browse.SendCurrentFileToRecycleBinAsync();
                    await browse.RedoLastOperationAsync();
                    Require(prompts == 0 && !Directory.EnumerateFiles(target).Any(), "Reader prompted or moved a file");
                    Require(snapshot.All(pair => File.Exists(pair.Key) && File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) &&
                        snapshot.Keys.Order().SequenceEqual(state.Files.Select(file => file.FullPath).Order()), "Reader modified files or replayed history");
                }
                finally { browse.DialogService = dialogs; }
            });
            await Check("Reader live insertion removal and same-metadata content changes preserve surviving pages", async () =>
            {
                await Jump("b.png"); await WaitUntil(() => Source("a.png") is not null && Source("b.png") is not null);
                var warm = Source("b.png"); var anchor = state.Reader.Anchor;
                var added = Path.Combine(directory, "0.png"); File.Copy(Path.Combine(directory, "a.png"), added);
                await monitor.Emit(new([added], [], false));
                await WaitUntil(() => state.Reader.Pages.Count == 5 && !(bool)Field(Reader(), "_restore")!);
                Require(state.Reader.Anchor?.Path == anchor?.Path && ReferenceEquals(warm, Source("b.png")), "Insertion reset surviving pixels/anchor");
                var changed = state.Files.Single(file => file.Name == "b.png");
                var originalBytes = File.ReadAllBytes(changed.FullPath);
                File.WriteAllBytes(changed.FullPath, new byte[originalBytes.Length]);
                File.SetLastWriteTimeUtc(changed.FullPath, changed.LastModifiedUtc);
                await monitor.Emit(new([changed.FullPath], [], false));
                await WaitUntil(() => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>().Any(row =>
                    ((MediFiler3.Domain.Files.FileEntry)row.GetType().GetProperty("File")!.GetValue(row)!).Name == "b.png" &&
                    ((Microsoft.UI.Xaml.Controls.TextBlock)row.GetType().GetProperty("Status")!.GetValue(row)!).Text.StartsWith("Cannot display")));
                File.WriteAllBytes(changed.FullPath, originalBytes); File.SetLastWriteTimeUtc(changed.FullPath, changed.LastModifiedUtc);
                await monitor.Emit(new([changed.FullPath], [], false));
                await WaitUntil(() => Source("b.png") is not null && !ReferenceEquals(warm, Source("b.png")));
                File.Delete(changed.FullPath); await monitor.Emit(new([changed.FullPath], [], true)); await Settled("c.png");
                File.Delete(Path.Combine(directory, "d.png")); await monitor.Emit(new([Path.Combine(directory, "d.png")], [], false));
                navigation.SelectDestination("Home"); await WaitUntil(() => monitor.Token.IsCancellationRequested);
                File.Copy(Path.Combine(directory, "a.png"), Path.Combine(directory, "e.png"));
                navigation.SelectDestination("Reader"); await WaitUntil(() => !monitor.Token.IsCancellationRequested);
                await monitor.Emit(new([], [], false, ReconcileRoots: [directory]));
                await WaitUntil(() => state.Reader.Pages.Any(file => file.Name == "e.png"));
                Require(state.CurrentFile?.Name == "c.png", "Offline reconciliation lost surviving current page");
                await Jump("c.png"); await WaitUntil(() => Source("c.png") is not null);
                var beforeRescan = Source("c.png");
                await monitor.Emit(new([], [directory], true));
                await WaitUntil(() => Source("c.png") is not null && !ReferenceEquals(beforeRescan, Source("c.png")));
                Require(state.CurrentFile?.Name == "c.png", "Overflow/rescan lost current page");
            });
            await Check("Reader folder rename maps the anchor and empty projections reject file mutations", async () =>
            {
                // Renaming the loaded root exercises descendant anchor mapping and node replacement.
                await Jump("c.png"); var before = state.Reader.Anchor!;
                dialogs.Name = "reader-mutations-renamed";
                await browse.ExecuteFolderMenuActionAsync(state.Roots[0], "rename-folder");
                await Settled("c.png");
                Require(state.Reader.Anchor!.Path.StartsWith(state.ActiveFolder!.Path, StringComparison.OrdinalIgnoreCase) &&
                    Math.Abs(state.Reader.Anchor.Fraction - before.Fraction) < .03, "Folder rename lost Reader anchor");
                var images = state.Files.Where(ReaderSessionState.Supports).Select(file => file.FullPath).ToArray();
                foreach (var image in images) File.Delete(image);
                await monitor.Emit(new(images, [], false));
                await WaitUntil(() => state.Reader.Pages.Count == 0 && Reader().RealizedCount == 0);
                var unsupported = state.Files.Single();
                await browse.PromptRenameCurrentFileAsync(); await browse.SendCurrentFileToRecycleBinAsync();
                Require(File.Exists(unsupported.FullPath) && !((Microsoft.UI.Xaml.Controls.AppBarButton)Field(toolbar, "_deleteFileButton")!).IsEnabled,
                    "Empty Reader mutated unsupported file or enabled delete");
            });
        }
        finally { browse.DialogService = originalDialogs; }
    }
    private sealed class ReaderScopedDialogs : IWinUiDialogService
    {
        public Func<string?>? OnPrompt;
        public Task<string?> PromptTextAsync(string title, string message, string initialValue = "", string primaryAction = "OK") => Task.FromResult(OnPrompt?.Invoke());
        public Task<bool> ConfirmAsync(string title, string message, string confirmAction) => Task.FromResult(false);
        public Task<FileCollisionResolution> ResolveCollisionAsync(FileCollision collision) => Task.FromResult(new FileCollisionResolution(FileCollisionChoice.Cancel));
    }

}
