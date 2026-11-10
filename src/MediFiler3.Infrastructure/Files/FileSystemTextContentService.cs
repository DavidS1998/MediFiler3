using System.Text;
using System.IO;
using MediFiler3.Application.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed class FileSystemTextContentService(IFileContentService files) : ITextContentService
{
    public const int WindowBytes = 1024 * 1024;

    public Task<TextWindow> ReadWindowAsync(string path, long offset, CancellationToken cancellationToken) =>
        Task.Run(async () =>
        {
            await using var stream = await files.OpenReadAsync(path, cancellationToken);
            var header = new byte[4];
            var headerLength = await stream.ReadAtLeastAsync(header, 4, throwOnEndOfStream: false, cancellationToken);
            var (encoding, bom) = DetectEncoding(header.AsSpan(0, headerLength));
            var start = offset == 0 ? bom : offset;
            if (start < bom || start > stream.Length) throw new IOException("The text file changed; reopen it to read from the beginning.");
            stream.Position = start;
            var length = stream.Length;
            var bytes = new byte[(int)Math.Min(WindowBytes, length - start)];
            var read = await stream.ReadAtLeastAsync(bytes, bytes.Length, throwOnEndOfStream: false, cancellationToken);
            if (read != bytes.Length || stream.Length != length)
                throw new IOException("The text file changed during reading; reopen it to read from the beginning.");
            string text;
            int consumed;
            try
            {
                var characters = new char[encoding.GetMaxCharCount(read)];
                // Do not flush a non-final window: leave a partial Unicode code point for the next read.
                encoding.GetDecoder().Convert(bytes, 0, read, characters, 0, characters.Length,
                    flush: start + read == length, out _, out var written, out _);
                text = new string(characters, 0, written);
                consumed = encoding.GetByteCount(text);
            }
            catch (DecoderFallbackException exception)
            {
                throw new InvalidDataException("Unsupported or malformed text encoding. Use UTF-8 or BOM-marked UTF-16/UTF-32.", exception);
            }
            long words = 0;
            var inWord = false;
            for (var index = 0; index < text.Length; index++)
            {
                if (index % 4096 == 0) cancellationToken.ThrowIfCancellationRequested();
                var character = text[index];
                if (char.IsControl(character) && !char.IsWhiteSpace(character))
                    throw new InvalidDataException("This file appears to contain binary data and cannot be displayed as text.");
                if (char.IsWhiteSpace(character)) inWord = false;
                else if (!inWord) { words++; inWord = true; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new TextWindow(text, encoding.WebName, start, start + consumed, length, words, offset == 0);
        }, cancellationToken);

    private static (Encoding Encoding, int Bom) DetectEncoding(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith(new byte[] { 0xff, 0xfe, 0, 0 })) return (new UTF32Encoding(false, false, true), 4);
        if (header.StartsWith(new byte[] { 0, 0, 0xfe, 0xff })) return (new UTF32Encoding(true, false, true), 4);
        if (header.StartsWith(new byte[] { 0xef, 0xbb, 0xbf })) return (new UTF8Encoding(false, true), 3);
        if (header.StartsWith(new byte[] { 0xff, 0xfe })) return (new UnicodeEncoding(false, false, true), 2);
        if (header.StartsWith(new byte[] { 0xfe, 0xff })) return (new UnicodeEncoding(true, false, true), 2);
        return (new UTF8Encoding(false, true), 0);
    }
}
