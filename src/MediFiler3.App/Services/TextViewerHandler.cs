using MediFiler3.Application.Files;
using MediFiler3.Application.Viewing;
using MediFiler3.Domain.Files;

namespace MediFiler3.Services;

public sealed class TextViewerHandler(ITextContentService text) : IWindowedViewerHandler<ViewerPresentation>
{
    public bool CanHandle(FileEntry file) => TextFileTypes.Supports(file);
    public ViewerCapabilities Capabilities => ViewerCapabilities.DocumentScroll;
    public Task<ViewerContent<ViewerPresentation>> LoadAsync(FileEntry file, int width, int height, CancellationToken token) =>
        LoadWindowAsync(file, 0, token);
    public async Task<ViewerContent<ViewerPresentation>> LoadWindowAsync(FileEntry file, long offset, CancellationToken token)
    {
        var window = await text.ReadWindowAsync(file.FullPath, offset, token);
        return new(new TextPresentation(window), new(window.FileLength, WordCount: window.WordCount, IsPartialWordCount: window.IsPartial));
    }
}

