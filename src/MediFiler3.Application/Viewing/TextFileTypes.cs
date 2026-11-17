using MediFiler3.Domain.Files;

namespace MediFiler3.Application.Viewing;

public static class TextFileTypes
{
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".txt", ".md", ".log", ".csv", ".json", ".xml", ".yaml", ".yml", ".ini", ".cfg", ".config",
      ".cs", ".xaml", ".html", ".htm", ".css", ".js", ".ts", ".sql", ".ps1", ".sh", ".srt", ".nfo", ".toml" };

    public static bool Supports(FileEntry file) => Extensions.Contains(file.Extension) ||
        file.Name.Equals("README", StringComparison.OrdinalIgnoreCase) || file.Name.Equals("LICENSE", StringComparison.OrdinalIgnoreCase);
}
