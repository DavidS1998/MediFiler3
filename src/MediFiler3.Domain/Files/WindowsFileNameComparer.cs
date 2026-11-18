using System.Runtime.InteropServices;

namespace MediFiler3.Domain.Files;

public sealed class WindowsFileNameComparer : IComparer<string>
{
    public static WindowsFileNameComparer Instance { get; } = new();

    public int Compare(string? left, string? right) =>
        left is null ? (right is null ? 0 : -1) : right is null ? 1 : StrCmpLogicalW(left, right);

    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int StrCmpLogicalW(string left, string right);
}
