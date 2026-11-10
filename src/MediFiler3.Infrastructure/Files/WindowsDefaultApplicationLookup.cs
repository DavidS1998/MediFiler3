using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using MediFiler3.Application.Files;

namespace MediFiler3.Infrastructure.Files;

public sealed class WindowsDefaultApplicationLookup : IDefaultApplicationLookup
{
    public DefaultApplicationInfo Query(string extension)
    {
        Marshal.ThrowExceptionForHR(CoInitializeEx(0, 2 | 4));
        try
        {
            var (progId, error) = QueryString(extension, 20); // ASSOCSTR_PROGID: effective user association.
            if (error == unchecked((int)0x80070483) || string.Equals(progId, "Unknown", StringComparison.OrdinalIgnoreCase))
                return new(DefaultApplicationStatus.None);
            var name = QueryString(extension, 4).Value; // ASSOCSTR_FRIENDLYAPPNAME
            var appId = QueryString(extension, 21).Value;
            var executable = QueryString(extension, 2).Value;
            byte[]? icon = null;
            // AppsFolder resolves packaged applications without guessing their installation/resource paths.
            if (!string.IsNullOrWhiteSpace(appId))
            {
                var app = ReadShellItem("shell:AppsFolder\\" + appId);
                if (!string.IsNullOrWhiteSpace(app.Name)) name = app.Name;
                icon = app.Icon;
            }
            if (icon is null && !string.IsNullOrWhiteSpace(executable)) icon = ReadShellItem(executable).Icon;
            if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(executable)) name = Path.GetFileNameWithoutExtension(executable);
            return new(!string.IsNullOrWhiteSpace(progId) || !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(appId)
                ? DefaultApplicationStatus.Available : DefaultApplicationStatus.Unknown, name, icon);
        }
        finally { CoUninitialize(); }
    }

    private static (string? Value, int Error) QueryString(string extension, uint kind)
    {
        uint length = 0;
        const uint flags = 0x20 | 0x400; // NOTRUNCATE | INIT_IGNOREUNKNOWN: the chooser is not a default app.
        var error = AssocQueryStringW(flags, kind, extension, null, null, ref length);
        if (length == 0 || length > 32768) return (null, error);
        var buffer = new StringBuilder((int)length);
        error = AssocQueryStringW(flags, kind, extension, null, buffer, ref length);
        return (error == 0 ? buffer.ToString() : null, error);
    }

    private static (string? Name, byte[]? Icon) ReadShellItem(string parsingName)
    {
        IShellItem? item = null;
        nint bitmap = 0;
        nint displayName = 0;
        string? name = null;
        try
        {
            var iid = typeof(IShellItem).GUID;
            if (SHCreateItemFromParsingName(parsingName, 0, ref iid, out item) < 0 || item is null) return (null, null);
            if (item.GetDisplayName(0, out displayName) == 0) name = Marshal.PtrToStringUni(displayName);
            if (((IShellItemImageFactory)item).GetImage(new NativeSize(32, 32), 4, out bitmap) < 0 || bitmap == 0) return (name, null);
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return (name, stream.ToArray());
        }
        catch (Exception exception) when (exception is COMException or InvalidCastException or ArgumentException or NotSupportedException or
            IOException or InvalidOperationException)
        { return (name, null); }
        finally
        {
            if (displayName != 0) Marshal.FreeCoTaskMem(displayName);
            if (bitmap != 0) DeleteObject(bitmap);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    [StructLayout(LayoutKind.Sequential)] private readonly struct NativeSize(int width, int height)
    { public readonly int Width = width; public readonly int Height = height; }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(nint context, ref Guid handler, ref Guid iid, out nint result);
        [PreserveSig] int GetParent(out IShellItem parent);
        [PreserveSig] int GetDisplayName(uint kind, out nint name);
        [PreserveSig] int GetAttributes(uint mask, out uint attributes);
        [PreserveSig] int Compare(IShellItem other, uint hint, out int order);
    }
    [ComImport, Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory { [PreserveSig] int GetImage(NativeSize size, uint flags, out nint bitmap); }
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int AssocQueryStringW(uint flags, uint kind, string association, string? extra, StringBuilder? output, ref uint length);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHCreateItemFromParsingName(string path, nint context, ref Guid iid, out IShellItem item);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint handle);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint flags);
    [DllImport("ole32.dll")] private static extern void CoUninitialize();
}
