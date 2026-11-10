using System.Globalization;

namespace MediFiler3.Application.Viewing;

public static class ViewerMetadataFormatter
{
    public static string Format(ViewerMetadata? metadata, CultureInfo? culture = null)
    {
        if (metadata is null) return string.Empty;
        culture ??= CultureInfo.InvariantCulture;
        var parts = new List<string> { FormatSize(metadata.FileSize, culture) };
        if (metadata.WordCount is { } words)
            parts.Add($"{words.ToString("N0", culture)} {(words == 1 ? "word" : "words")}{(metadata.IsPartialWordCount ? " (window)" : "")}");
        if (metadata.PixelWidth is > 0 && metadata.PixelHeight is > 0)
            parts.Add($"[{metadata.PixelWidth.Value.ToString(culture)}×{metadata.PixelHeight.Value.ToString(culture)}]");
        if (metadata.Duration is { } duration && duration >= TimeSpan.Zero)
            parts.Add(duration.TotalHours >= 1
                ? $"[{((long)duration.TotalHours).ToString(culture)}:{duration.Minutes:00}:{duration.Seconds:00}]"
                : $"[{duration.Minutes:00}:{duration.Seconds:00}]");
        return string.Join(" - ", parts);
    }

    private static string FormatSize(long bytes, CultureInfo culture)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
        var size = (decimal)Math.Max(0, bytes);
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1) { size /= 1024; unit++; }
        return $"{size.ToString("0.0", culture)} {units[unit]}";
    }
}
