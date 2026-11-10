using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;

namespace MediFiler3.Services;

public abstract record ViewerPresentation;
public sealed record BitmapPresentation(BitmapSource Source) : ViewerPresentation;
public sealed record TextPresentation(TextWindow Window) : ViewerPresentation;

