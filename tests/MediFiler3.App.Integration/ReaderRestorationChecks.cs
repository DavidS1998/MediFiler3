using System.IO;
using System.Reflection;
using System.Windows;
using MediFiler3.Application.Files;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Interop;
using Microsoft.UI.Xaml.Controls;

internal static partial class Program
{
    private static async Task RunReaderRestorationChecksAsync()
    {
        await Check("Reader preserves a pending native fractional scroll through late source dimensions and width reflow", async () =>
        {
            var directory = Directory.CreateDirectory(Path.Combine(Root, "reader-scroll-dimension-race")).FullName;
            var files = Enumerable.Range(1, 12).Select(index =>
            {
                var path = Path.Combine(directory, $"page {index}.png");
                File.Copy(Fixtures[".png"].FullPath, path);
                return Entry(path);
            }).ToArray();
            var session = new ReaderSessionState();
            session.SetFiles(directory, files, files[3], true);
            var decoded = await new FileSystemReaderImageService(Files).DecodeAsync(files[0], 128, default);
            // Only page 7 has undiscovered dimensions. Its real source pixels are held until
            // the native ChangeView request is accepted, matching the captured failure.
            foreach (var file in files.Where(file => file != files[6]))
                session.SetDimensions(file, decoded.OriginalWidth, decoded.OriginalHeight);
            var images = new GatedReaderImages(files[6]);
            var reader = new WinUiReaderIsland(session, images);
            var window = new Window { Width = 1000, Height = 700, Content = reader,
                Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            try
            {
                reader.SetFiles(directory, files, files[3], true); reader.SetActive(true); window.Show();
                var held = await images.Ready.Task.WaitAsync(TimeSpan.FromSeconds(5));
                bool Settled() => !(bool)Field(reader, "_restore")! && !(bool)Field(reader, "_queued")!;
                var scroll = (ScrollViewer)Field(reader, "_scroll")!;
                await WaitUntil(() => Settled() && session.Anchor?.Path == files[3].FullPath &&
                    Math.Abs(scroll.VerticalOffset - session.Top(3)) < .5);
                using var diagnostics = new ReaderReflowDiagnostics(reader, session, "reader-scroll-dimension-race");
                var target = session.Top(3) + session.Height(3) * .2;
                var deliveredDuringLayout = false;
                void Changed(object? sender, ScrollViewerViewChangedEventArgs args)
                {
                    if ((bool)Field(reader, "_updating")! && Math.Abs(scroll.VerticalOffset - target) < .5)
                        deliveredDuringLayout = true;
                }
                scroll.ViewChanged += Changed;
                try
                {
                    diagnostics.Mark("probe.request", $"target={target}");
                    Require(scroll.ChangeView(null, target, null, true), "Native fractional scroll was not accepted");
                    Require(Math.Abs(scroll.VerticalOffset - session.Top(3)) < .5,
                        "Regression setup did not leave the accepted native scroll pending");
                    // Complete the actual decode inline on this dispatcher, then run the queued
                    // layout before yielding. No sleeps or production test hooks force the race.
                    images.Publish(held);
                    Require((bool)Field(reader, "_restore")!, "Late source dimensions did not request restoration");
                    diagnostics.Mark("dimensions.applied");
                    typeof(WinUiReaderIsland).GetMethod("UpdatePages", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(reader, null);
                    Require(deliveredDuringLayout, "Regression setup did not deliver the native scroll during layout");
                    try
                    {
                        await WaitUntil(() => Settled() && session.Anchor?.Path == files[3].FullPath &&
                            Math.Abs(session.Anchor.Fraction - .2) < .03 && Math.Abs(scroll.VerticalOffset - target) < .5);
                    }
                    catch (TimeoutException exception)
                    {
                        throw new InvalidOperationException($"Late dimensions overwrote the pending native scroll: anchor={session.Anchor}; " +
                            $"offset={scroll.VerticalOffset}; target={target}; restore={Field(reader, "_restore")}; queued={Field(reader, "_queued")}", exception);
                    }
                    diagnostics.Mark("probe.settled");
                    reader.SetOptions(session.Options with { WidthPercent = 50 });
                    await WaitUntil(() => Settled() && Math.Abs(scroll.VerticalOffset - session.Offset(session.Anchor)) < .5);
                    Require(session.Anchor?.Path == files[3].FullPath && Math.Abs(session.Anchor.Fraction - .2) < .03 &&
                        Math.Abs(scroll.VerticalOffset - (session.Top(3) + session.Height(3) * .2)) < .5,
                        "Width reflow lost the recovered native pixel/fraction anchor");
                }
                finally { scroll.ViewChanged -= Changed; }
            }
            finally
            {
                reader.SetActive(false); images.Release.TrySetCanceled();
                window.Content = null; reader.Dispose(); window.Close();
            }
        });
    }

    private sealed class GatedReaderImages(FileEntry heldFile) : IReaderImageService
    {
        private readonly FileSystemReaderImageService _actual = new(Files);
        private SynchronizationContext? _publicationContext;
        public TaskCompletionSource<ReaderImage> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // Inline continuation is deliberate: the test orders decode publication and native
        // layout in one dispatcher turn instead of depending on worker or timer timing.
        public TaskCompletionSource<ReaderImage> Release { get; } = new();
        public Task<ReaderImage> DecodeAsync(FileEntry file, int displayPixelWidth, CancellationToken token)
        {
            if (file != heldFile) return _actual.DecodeAsync(file, displayPixelWidth, token);
            _publicationContext = SynchronizationContext.Current;
            _ = PrepareAsync(file, displayPixelWidth, token);
            return Release.Task.WaitAsync(token);
        }
        private async Task PrepareAsync(FileEntry file, int width, CancellationToken token)
        {
            try { Ready.TrySetResult(await _actual.DecodeAsync(file, width, token)); }
            catch (Exception exception) { Ready.TrySetException(exception); }
        }
        public void Publish(ReaderImage image)
        {
            // LoadRowAsync captures this Render-priority UI context. Use that same context
            // for completion so its continuation runs inline instead of being posted behind
            // the pending native scroll. Both contexts belong to this test's dispatcher.
            var previous = SynchronizationContext.Current;
            try { SynchronizationContext.SetSynchronizationContext(_publicationContext); Release.SetResult(image); }
            finally { SynchronizationContext.SetSynchronizationContext(previous); }
        }
    }
}
