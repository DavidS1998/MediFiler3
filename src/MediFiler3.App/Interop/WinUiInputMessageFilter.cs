using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MediFiler3.Interop;

internal static class WinUiInputMessageFilter
{
    private static bool _installed;

    public static void Install()
    {
        if (_installed)
        {
            return;
        }

        ComponentDispatcher.ThreadFilterMessage += FilterMessage;
        _installed = true;
    }

    private static void FilterMessage(ref MSG message, ref bool handled)
    {
        if (handled)
        {
            return;
        }

        var nativeMessage = new NativeMessage
        {
            WindowHandle = message.hwnd,
            Message = unchecked((uint)message.message),
            WParam = message.wParam,
            LParam = message.lParam,
            Time = unchecked((uint)message.time),
            Point = new NativePoint(message.pt_x, message.pt_y)
        };

        handled = ContentPreTranslateMessage(ref nativeMessage);
    }

    [DllImport("Microsoft.UI.Windowing.Core.dll", ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ContentPreTranslateMessage(ref NativeMessage message);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr WindowHandle;
        public uint Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}
