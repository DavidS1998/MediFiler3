using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediFiler3.Infrastructure.Files;
using MediFiler3.Services;
using MediFiler3.Views;

internal static partial class Program
{
    private static async Task RunBrowseFallbackChecksAsync()
    {
        await Check("Browse displays real PSD and ICO Windows previews alongside fallback messages through resize", async () =>
        {
            var psd = WriteText("fallback.psd", "unsupported PSD content", new System.Text.UTF8Encoding(false));
            var pixels = new byte[32 * 32 * 4];
            for (var i = 0; i < pixels.Length; i += 4) { pixels[i + 1] = 210; pixels[i + 3] = 255; }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32, 32, 96, 96, PixelFormats.Bgra32, null, pixels, 128)));
            using var png = new MemoryStream(); encoder.Save(png);
            var icoPath = Path.Combine(Root, "fallback.ico");
            using (var writer = new BinaryWriter(File.Create(icoPath)))
            {
                writer.Write((ushort)0); writer.Write((ushort)1); writer.Write((ushort)1);
                writer.Write((byte)32); writer.Write((byte)32); writer.Write((byte)0); writer.Write((byte)0);
                writer.Write((ushort)1); writer.Write((ushort)32); writer.Write((uint)png.Length); writer.Write((uint)22);
                writer.Write(png.ToArray());
            }
            var viewer = new ViewerHostView();
            viewer.Initialize(new WicImageDecoder(Files), NativeThumbnails, new FileSystemTextContentService(Files), new External(), new Defaults());
            var window = new Window { Content = viewer, Width = 1280, Height = 800, Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            window.Show();
            try
            {
                foreach (var file in new[] { psd, Entry(icoPath) })
                {
                    await viewer.LoadAsync(file, true);
                    var image = (Image)viewer.FindName("ImageViewer");
                    await WaitUntil(() => image.Source is BitmapSource && image.Visibility == Visibility.Visible);
                    Require(((FrameworkElement)viewer.FindName("ExternalFallbackPanel")).Visibility == Visibility.Visible &&
                        ((FrameworkElement)viewer.FindName("ImageMessagePanel")).Visibility == Visibility.Collapsed,
                        "Fallback message obscured " + file.Name);
                    window.Width = window.Width == 1280 ? 1000 : 1280;
                    await Task.Delay(400);
                    Require(image.Source is BitmapSource && image.Visibility == Visibility.Visible, "Resize removed " + file.Name);
                    window.UpdateLayout();
                    var capture = new RenderTargetBitmap(Math.Max(1, (int)Math.Ceiling(image.ActualWidth)),
                        Math.Max(1, (int)Math.Ceiling(image.ActualHeight)), 96, 96, PixelFormats.Pbgra32);
                    capture.Render(image);
                    var surfacePixels = new byte[capture.PixelWidth * capture.PixelHeight * 4];
                    capture.CopyPixels(surfacePixels, capture.PixelWidth * 4, 0);
                    Require(Enumerable.Range(0, surfacePixels.Length / 4).Any(index => surfacePixels[index * 4 + 3] > 200),
                        "Browse image surface did not render " + file.Name);
                    var bitmap = (BitmapSource)image.Source;
                    var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
                    var rendered = new byte[converted.PixelWidth * converted.PixelHeight * 4];
                    converted.CopyPixels(rendered, converted.PixelWidth * 4, 0);
                    Require(Enumerable.Range(0, rendered.Length / 4).Any(index => rendered[index * 4 + 3] > 0), "Empty preview pixels");
                    if (file.Extension == ".ico")
                        Require(Enumerable.Range(0, rendered.Length / 4).Any(index => rendered[index * 4 + 1] > 180 && rendered[index * 4 + 2] < 50 && rendered[index * 4 + 3] > 200),
                            "ICO preview does not contain the icon's actual green pixels");
                }
                var current = Entry(icoPath);
                var retained = (BitmapSource)((Image)viewer.FindName("ImageViewer")).Source;
                viewer.Initialize(new WicImageDecoder(Files), new ThumbnailsWithoutPreview(), new FileSystemTextContentService(Files), new External(), new Defaults());
                await viewer.LoadAsync(current, true, retained);
                Require(ReferenceEquals(retained, ((Image)viewer.FindName("ImageViewer")).Source) &&
                    ((FrameworkElement)viewer.FindName("ImageMessagePanel")).Visibility == Visibility.Collapsed,
                    "Unavailable thumbnail overwrote an already available preview");
                await viewer.LoadAsync(psd, false);
                Require(((Image)viewer.FindName("ImageViewer")).Source is null,
                    "A preview from a different file survived a failed load");
            }
            finally { viewer.SetActive(false); viewer.Clear(); window.Content = null; window.Close(); }
        });
    }
    private sealed class ThumbnailsWithoutPreview : MediFiler3.Application.Files.IThumbnailService
    {
        public Task<byte[]?> GetThumbnailPngAsync(MediFiler3.Domain.Files.FileEntry file, int width, int height, CancellationToken token) => Task.FromResult<byte[]?>(null);
    }
}
