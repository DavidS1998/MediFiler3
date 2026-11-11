using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace MediFiler3.Views;

public partial class BrowseView
{
    private delegate nint DragMouseHook(int code, nint message, nint data);
    private nint _galleryDragWheelHook;
    private DragMouseHook? _galleryDragWheelCallback;
    private PendingDragWheel? _pendingDragWheel;
    private sealed class PendingDragWheel(GalleryMoveDrag drag, Point point, int delta)
    {
        public readonly GalleryMoveDrag Drag = drag;
        public Point Point = point;
        public int Delta = delta;
    }

    private void StartGalleryDragWheelHook()
    {
        StopGalleryDragWheelHook();
        _galleryDragWheelCallback ??= OnGalleryDragMouseHook;
        // WH_MOUSE_LL sees wheel input before the native OLE drag loop consumes it.
        _galleryDragWheelHook = SetDragWindowsHook(14, _galleryDragWheelCallback, DragGetModuleHandle(null), 0);
        if (_galleryDragWheelHook == 0)
            ShowOperationNotice($"Drag wheel scrolling could not be enabled (Windows error {Marshal.GetLastWin32Error()}). Edge scrolling remains available.");
    }

    private void StopGalleryDragWheelHook()
    {
        _pendingDragWheel = null;
        if (_galleryDragWheelHook == 0) return;
        UnhookDragWindowsHook(_galleryDragWheelHook);
        _galleryDragWheelHook = 0;
    }

    private nint OnGalleryDragMouseHook(int code, nint message, nint data)
    {
        // A hook exists only during our drag, and never intercepts input for another foreground app.
        if (code == 0 && message == 0x020A && _galleryDragWheelHook != 0 &&
            Window.GetWindow(this) is { IsActive: true })
        {
            try { if (RouteGalleryDragWheelPacket(data)) return 1; }
            catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or COMException)
            { /* Input outside a live presentation continues through the normal hook chain. */ }
        }
        return CallNextDragHook(_galleryDragWheelHook, code, message, data);
    }

    private bool RouteGalleryDragWheelPacket(nint data)
    {
        if (_galleryDragWheelHook == 0) return false;
        var wheel = Marshal.PtrToStructure<DragMouseWheelData>(data);
        var point = new Point(wheel.X, wheel.Y);
        var delta = (short)(wheel.MouseData >> 16);
        if (delta == 0 || !_galleryTreeDragInside || !GalleryDragIsCurrent() ||
            !IsInsideGalleryDragTree(FolderTree.PointFromScreen(point)) || _galleryMoveDrag is not { } drag) return false;
        // Do not run WPF layout inside the input hook. Coalesce and validate again on dispatch.
        if (_pendingDragWheel is { } pending && ReferenceEquals(pending.Drag, drag))
        {
            pending.Delta = (int)Math.Clamp((long)pending.Delta + delta, -12000, 12000);
            pending.Point = point;
        }
        else
        {
            var batch = new PendingDragWheel(drag, point, delta);
            _pendingDragWheel = batch;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (!ReferenceEquals(_pendingDragWheel, batch)) return;
                _pendingDragWheel = null;
                if (ReferenceEquals(_galleryMoveDrag, batch.Drag)) ScrollGalleryDragTreeWheel(batch.Delta, batch.Point);
            }));
        }
        // Consume this wheel once; do not also send it to the focused Gallery control.
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DragMouseWheelData
    {
        public int X, Y;
        public uint MouseData, Flags, Time;
        public nuint ExtraInfo;
    }
    [DllImport("user32.dll", EntryPoint = "SetWindowsHookExW", SetLastError = true)]
    private static extern nint SetDragWindowsHook(int id, DragMouseHook callback, nint module, uint thread);
    [DllImport("user32.dll", EntryPoint = "UnhookWindowsHookEx")]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookDragWindowsHook(nint hook);
    [DllImport("user32.dll", EntryPoint = "CallNextHookEx")]
    private static extern nint CallNextDragHook(nint hook, int code, nint message, nint data);
    [DllImport("kernel32.dll", EntryPoint = "GetModuleHandleW", CharSet = CharSet.Unicode)]
    private static extern nint DragGetModuleHandle(string? name);
}
