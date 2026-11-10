namespace MediFiler3.Application.Files;

public sealed record TextWindow(string Text, string EncodingName, long StartOffset, long NextOffset,
    long FileLength, long WordCount, bool IsFirstWindow = true)
{
    public bool HasNext => NextOffset < FileLength;
    public bool IsPartial => !IsFirstWindow || HasNext;
}

public interface ITextContentService
{
    Task<TextWindow> ReadWindowAsync(string path, long offset, CancellationToken cancellationToken);
}
