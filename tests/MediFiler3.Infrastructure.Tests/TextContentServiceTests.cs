using System.Text;
using MediFiler3.Application.Files;
using MediFiler3.Infrastructure.Files;

namespace MediFiler3.Infrastructure.Tests;

public sealed class TextContentServiceTests
{
    [Theory]
    [InlineData("utf8")]
    [InlineData("utf8bom")]
    [InlineData("utf16le")]
    [InlineData("utf16be")]
    [InlineData("utf32le")]
    [InlineData("utf32be")]
    public async Task ReadsSupportedEncodingsAndCountsWhitespaceSeparatedWords(string name)
    {
        var encoding = EncodingFor(name);
        const string text = "one,\ttwo\nthree-four\u2003five";
        var content = new Content(Encode(text, encoding));
        var result = await new FileSystemTextContentService(content).ReadWindowAsync("test", 0, CancellationToken.None);
        Assert.Equal(text, result.Text);
        Assert.Equal(4, result.WordCount);
        Assert.False(result.HasNext);
        Assert.False(result.IsPartial); // BOM bytes do not make this a partial-file count.
        Assert.True(content.LastStream!.WasDisposed);
    }

    [Theory]
    [InlineData("utf8")]
    [InlineData("utf16le")]
    public async Task WindowsPreserveUnicodeAcrossByteBoundariesAndCanBeReadAgain(string name)
    {
        var encoding = EncodingFor(name);
        var prefixLength = name == "utf8" ? FileSystemTextContentService.WindowBytes - 1
            : FileSystemTextContentService.WindowBytes / 2 - 1;
        var text = new string('a', prefixLength) + "😀 end";
        var content = new Content(Encode(text, encoding));
        var service = new FileSystemTextContentService(content);
        var first = await service.ReadWindowAsync("test", 0, CancellationToken.None);
        Assert.True(first.HasNext);
        Assert.True(first.IsPartial);
        Assert.True(content.LastStream!.BytesRead <= FileSystemTextContentService.WindowBytes + 4);
        Assert.True(first.NextOffset - first.StartOffset <= FileSystemTextContentService.WindowBytes);
        var second = await service.ReadWindowAsync("test", first.NextOffset, CancellationToken.None);
        Assert.Equal(text, first.Text + second.Text);
        Assert.False(second.HasNext);
        Assert.True(second.IsPartial);
        Assert.Equal(first, await service.ReadWindowAsync("test", 0, CancellationToken.None));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(255)]
    public async Task RejectsBinaryAndMalformedTextAndClosesStream(int invalidByte)
    {
        var content = new Content([(byte)'a', (byte)invalidByte, (byte)'b']);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemTextContentService(content)
            .ReadWindowAsync("test", 0, CancellationToken.None));
        Assert.True(content.LastStream!.WasDisposed);
    }

    [Fact]
    public async Task MalformedByteAtWindowBoundaryIsNotSilentlyTrimmed()
    {
        var bytes = Enumerable.Repeat((byte)'a', FileSystemTextContentService.WindowBytes + 1).ToArray();
        bytes[FileSystemTextContentService.WindowBytes - 1] = 255;
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemTextContentService(new Content(bytes))
            .ReadWindowAsync("test", 0, CancellationToken.None));
    }

    [Fact]
    public async Task EmptyFileHasZeroWordsAndNoMoreWindows()
    {
        var result = await new FileSystemTextContentService(new Content([])).ReadWindowAsync("empty", 0, CancellationToken.None);
        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.WordCount);
        Assert.False(result.IsPartial);
    }

    [Fact]
    public async Task CancellationBeforeAndDuringReadingIsHonored()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var content = new Content(Encoding.UTF8.GetBytes("some text"));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileSystemTextContentService(content)
            .ReadWindowAsync("test", 0, cancelled.Token));
        Assert.Null(content.LastStream);

        using var duringRead = new CancellationTokenSource();
        content.OnRead = duringRead.Cancel;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new FileSystemTextContentService(content)
            .ReadWindowAsync("test", 0, duringRead.Token));
        Assert.True(content.LastStream!.WasDisposed);
    }

    [Fact]
    public async Task OffsetPastChangedFileLengthIsReported()
    {
        await Assert.ThrowsAsync<IOException>(() => new FileSystemTextContentService(new Content([]))
            .ReadWindowAsync("changed", 10, CancellationToken.None));
    }

    private static Encoding EncodingFor(string name) => name switch
    {
        "utf8" => new UTF8Encoding(false), "utf8bom" => new UTF8Encoding(true),
        "utf16le" => new UnicodeEncoding(false, true), "utf16be" => new UnicodeEncoding(true, true),
        "utf32le" => new UTF32Encoding(false, true), _ => new UTF32Encoding(true, true)
    };
    private static byte[] Encode(string text, Encoding encoding) => [.. encoding.GetPreamble(), .. encoding.GetBytes(text)];

    private sealed class Content(byte[] bytes) : IFileContentService
    {
        public CountingStream? LastStream { get; private set; }
        public Action? OnRead { get; set; }
        public Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastStream = new CountingStream(bytes, () => OnRead?.Invoke());
            return Task.FromResult<Stream>(LastStream);
        }
    }

    private sealed class CountingStream(byte[] bytes, Action onRead) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public bool WasDisposed { get; private set; }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var count = await base.ReadAsync(buffer, cancellationToken);
            BytesRead += count;
            onRead();
            return count;
        }
        protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
    }
}
