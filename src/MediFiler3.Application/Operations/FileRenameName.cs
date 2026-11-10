using System.Globalization;

namespace MediFiler3.Application.Operations;

public static class FileRenameName
{
    public static string EditableName(string name, bool isDirectory = false) =>
        isDirectory ? name : name[..^Extension(name).Length];

    public static string CompleteName(string originalName, string editedName, bool isDirectory = false) =>
        editedName.Trim() + (isDirectory ? string.Empty : Extension(originalName));

    private static string Extension(string name) => name.LastIndexOf('.') > 0 ? Path.GetExtension(name) : string.Empty;

    public static string SuggestNumberedName(string name, bool isDirectory, Func<string, bool> exists)
    {
        var stem = EditableName(name, isDirectory);
        var extension = isDirectory ? string.Empty : Extension(name);
        // Continue an existing suffix instead of producing "Test (1) (1)".
        var number = 1L;
        var opening = stem.LastIndexOf(" (", StringComparison.Ordinal);
        if (opening >= 0 && stem.EndsWith(')') &&
            long.TryParse(stem.AsSpan(opening + 2, stem.Length - opening - 3), NumberStyles.None,
                CultureInfo.InvariantCulture, out var previous) && previous is > 0 and < long.MaxValue)
        {
            stem = stem[..opening];
            number = previous + 1;
        }
        while (true)
        {
            var candidate = $"{stem} ({number.ToString(CultureInfo.InvariantCulture)}){extension}";
            if (!exists(candidate)) return candidate;
            number = checked(number + 1);
        }
    }
}
