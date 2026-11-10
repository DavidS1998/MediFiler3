using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Xaml.Hosting;

namespace MediFiler3.Interop;

internal static class NativeSurfaceOrder
{
    public static void Raise(DesktopWindowXamlSource? source)
    {
        if (source?.SiteBridge is not { } bridge) return;
        var handle = Win32Interop.GetWindowFromWindowId(bridge.WindowId);
        SetWindowPos(handle, 0, 0, 0, 0, 0, 0x0013); // HWND_TOP, no move/size/activation
    }
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
