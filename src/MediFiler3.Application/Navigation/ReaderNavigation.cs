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
        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= 1 && number <= pages.Count)
            return number - 1;
        error = $"Enter a page number from 1 to {pages.Count}.";
        return -1;
    }
}
