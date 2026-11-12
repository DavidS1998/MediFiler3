using System.Globalization;
using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Navigation;

public enum ReaderNavigation { PreviousImage, NextImage, LineUp, LineDown, ViewUp, ViewDown, Start, End }

public static class ReaderPageLookup
{
    public static int Resolve(IReadOnlyList<FileEntry> pages, string? query, out string error)
    {
        error = "";
        var text = query?.Trim() ?? "";
        if (pages.Count == 0) { error = "There are no supported images in this folder."; return -1; }
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            if (number >= 1 && number <= pages.Count) return number - 1;
            error = $"Enter a page number from 1 to {pages.Count}."; return -1;
        }
        var matches = pages.Select((file, index) => (file, index)).Where(item =>
            StringComparer.OrdinalIgnoreCase.Equals(item.file.Name, text) ||
            StringComparer.OrdinalIgnoreCase.Equals(item.file.FullPath, text)).Take(2).ToArray();
        if (matches.Length == 1) return matches[0].index;
        error = matches.Length > 1 ? "This filename is ambiguous. Enter the full image path." : "Enter a page number or an exact image filename in this folder.";
        return -1;
    }
}
