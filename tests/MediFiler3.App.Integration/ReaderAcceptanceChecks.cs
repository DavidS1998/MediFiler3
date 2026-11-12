using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Navigation;
using MediFiler3.Domain.Files;
using MediFiler3.Interop;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunReaderAcceptanceChecksAsync(MediFiler3.MainWindow window, NavigationSessionState state,
        BrowseView browse, WinUiNavigationIsland navigation, RecordingReaderImages images)
    {
        await Check("Reader acceptance measures bounded source-image navigation and warm return in a 1000-file folder", async () =>
        {
            const int count = 1000, width = 1024, height = 1536;
            var directory = Directory.CreateDirectory(Path.Combine(Root, "reader-acceptance")).FullName;
            // Actual independently addressable PNG files and WIC decoding, with a reproducible
            // generated raster. This is not a user-library/FPS or process-memory ceiling benchmark.
            await Task.Run(() =>
            {
                var pixels = new byte[width * height * 4];
                for (var y = 0; y < height; y++) for (var x = 0; x < width; x++)
                {
                    var index = (y * width + x) * 4;
                    pixels[index] = (byte)(x / 8); pixels[index + 1] = (byte)(64 + y / 12);
                    pixels[index + 2] = (byte)((x + y) / 16); pixels[index + 3] = 255;
                }
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
                var template = Path.Combine(directory, "page-0000.png");
                using (var stream = File.Create(template)) encoder.Save(stream);
                for (var i = 1; i < count; i++) File.Copy(template, Path.Combine(directory, $"page-{i:D4}.png"));
            });
            WinUiReaderIsland Reader() => (WinUiReaderIsland)Field(browse, "_reader")!;
            object? Source(int index) => ((System.Collections.IDictionary)Field(Reader(), "_rows")!).Values.Cast<object>()
                .Where(row => Equals(row.GetType().GetProperty("File")!.GetValue(row), state.Reader.Pages[index]))
                .Select(row => ((Microsoft.UI.Xaml.Controls.Image)row.GetType().GetProperty("Image")!.GetValue(row)!).Source).FirstOrDefault();
            async Task Ready(int index) => await WaitUntil(() => state.Reader.Pages.Count == count && browse.ReaderPageIndex == index &&
                state.CurrentFile == state.Reader.Pages[index] && Source(index) is not null &&
                !(bool)Field(Reader(), "_restore")! && !(bool)Field(Reader(), "_queued")!);
            var callsBefore = images.Calls;
            var timer = Stopwatch.StartNew();
            await LoadRestartRootsAsync(window, [directory]);
            await WaitUntil(() => state.Files.Count == count);
            browse.SetReaderOptions(new()); navigation.SelectDestination("Reader");
            Require(browse.JumpReaderPage(0), "Acceptance entry jump failed"); await Ready(0);
            var entryMs = timer.Elapsed.TotalMilliseconds;
            var samples = new List<object>(); var maxRows = 0; long maxBytes = 0;
            using var process = Process.GetCurrentProcess();
            void Sample(int target, double milliseconds)
            {
                process.Refresh(); maxRows = Math.Max(maxRows, Reader().RealizedCount); maxBytes = Math.Max(maxBytes, Reader().DecodedBytes);
                Require(Reader().RealizedCount is > 0 and < 30 && Reader().DecodedBytes <= WinUiReaderIsland.CacheLimit && images.MaximumConcurrent <= 2,
                    "Source-image acceptance escaped Reader row/cache/worker bounds");
                samples.Add(new { PageIndex = target, ReadyMs = milliseconds, RealizedRows = Reader().RealizedCount,
                    DecodedCacheBytes = Reader().DecodedBytes, ProcessWorkingSetBytes = process.WorkingSet64,
                    ProcessPrivateBytes = process.PrivateMemorySize64, ManagedHeapBytes = GC.GetTotalMemory(false) });
            }
            Sample(0, entryMs);
            foreach (var target in new[] { 125, 500, 999, 250, 0, 500 })
            {
                timer.Restart(); Require(browse.JumpReaderPage(target), "Acceptance distant jump failed"); await Ready(target);
                Sample(target, timer.Elapsed.TotalMilliseconds);
            }
            var anchor = state.Reader.Anchor; var warm = Source(500);
            navigation.SelectDestination("Home"); await WaitUntil(() => !Reader().IsActive && Reader().RealizedCount == 0);
            var inactiveCalls = images.Calls; await Task.Delay(150);
            var inactiveRequests = images.Calls - inactiveCalls;
            Require(inactiveRequests == 0, "Inactive acceptance Reader scheduled source decoding");
            timer.Restart(); navigation.SelectDestination("Reader"); await Ready(500);
            var returnMs = timer.Elapsed.TotalMilliseconds;
            Require(ReferenceEquals(warm, Source(500)) && state.Reader.Anchor?.Path == anchor?.Path &&
                Math.Abs((state.Reader.Anchor?.Fraction ?? 0) - (anchor?.Fraction ?? 0)) < .03, "Acceptance warm return lost source pixels or anchor");
            Sample(500, returnMs);
            var evidence = new
            {
                Corpus = "1000 PNG copies of a deterministic generated 1024x1536 raster; actual independent files/WIC source decodes",
                FileCount = count, SourceWidth = width, SourceHeight = height,
                CorpusBytes = new FileInfo(Path.Combine(directory, "page-0000.png")).Length * count,
                FolderListingAndReaderEntryReadyMs = entryMs, WarmReturnReadyMs = returnMs,
                ReadyDefinition = "Selected page source assigned and native offset restoration/render queue settled; not compositor FPS",
                ObservedMaximumRealizedRows = maxRows, ObservedMaximumDecodedCacheBytes = maxBytes,
                MaximumConcurrentSourceDecodesInReaderRun = images.MaximumConcurrent,
                SourceDecodeCallsInScenario = images.Calls - callsBefore, InactiveObservationMs = 150,
                SourceRequestsDuringInactiveObservation = inactiveRequests,
                WarmCurrentSourceReused = ReferenceEquals(warm, Source(500)),
                ProcessMemoryScope = "Sample points for the entire hidden integration process, including preceding tests; not Reader-only allocations or ceilings",
                Limits = "Generated local corpus; no user-library, remote/locked/removable-storage, mixed-DPI, Narrator, touchpad, sustained frame-time or process-memory guarantee",
                Samples = samples
            };
            var path = Path.Combine(Root, "reader-acceptance.json");
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("Reader acceptance evidence: " + path);
        });
    }
}
