using System.Runtime.InteropServices;

namespace MediFiler3.Infrastructure.Files;

public sealed class WindowsFileLaunchAdapter : IWindowsFileLaunchAdapter
{
    public int OpenDefault(string fullPath, nint ownerWindow) => WithCom(() =>
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(), Mask = 0x100 | 0x400, // NOASYNC, FLAG_NO_UI
            Owner = ownerWindow, File = fullPath, Show = 1
        };
        if (ShellExecuteExW(ref info)) return 0;
        var error = Marshal.GetLastWin32Error();
        return error != 0 ? error : info.Instance.ToInt64() is > 0 and <= 32 ? (int)info.Instance : 1;
    });

    public int OpenWith(string fullPath, nint ownerWindow) => WithCom(() =>
    {
        var info = new OpenAsInfo { File = fullPath, Flags = 4 }; // OAIF_EXEC: open this file using the choice.
        return SHOpenWithDialog(ownerWindow, ref info);
    });

    private static int WithCom(Func<int> invoke)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2 | 4));
        try { return invoke(); }
        finally { CoUninitialize(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenAsInfo { public string File; public string? Class; public uint Flags; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size; public uint Mask; public nint Owner;
        public string? Verb; public string File; public string? Parameters; public string? Directory;
        public int Show; public nint Instance; public nint IdList; public string? Class;
        public nint ClassKey; public uint HotKey; public nint Icon; public nint Process;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShellExecuteExW(ref ShellExecuteInfo info);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SHOpenWithDialog(nint owner, ref OpenAsInfo info);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
