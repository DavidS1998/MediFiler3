using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MediFiler3.Application.Persistence;

namespace MediFiler3.Interop;

public interface IWindowPlacementPlatform
{
    IReadOnlyList<MonitorWorkArea> GetMonitors();
    WindowLayout Capture(Window window, bool maximized, double folderTreeWidth);
    void Apply(Window window, WindowPlacement placement, bool beforeFirstShow);
}

public sealed class WindowsWindowPlacement : IWindowPlacementPlatform
{
    public IReadOnlyList<MonitorWorkArea> GetMonitors()
    {
        var result = new List<MonitorWorkArea>();
        MonitorCallback callback = (nint monitor, nint dc, ref NativeRect rectangle, nint data) =>
        {
            try { result.Add(Describe(monitor)); }
            catch (Win32Exception) { /* A monitor can disappear during enumeration. */ }
            return true;
        };
        if (!EnumDisplayMonitors(0, 0, callback, 0) || result.Count == 0)
        {
            var area = SystemParameters.WorkArea;
            var scale = Math.Max(96, GetDpiForSystem()) / 96d;
            result.Clear();
            result.Add(new("primary", area.Left * scale, area.Top * scale, area.Width * scale, area.Height * scale, scale, true));
        }
        return result;
    }

    public WindowLayout Capture(Window window, bool maximized, double folderTreeWidth)
    {
        var handle = new WindowInteropHelper(window).Handle;
        NativeRect rectangle;
        if (window.WindowState == WindowState.Normal)
        {
            if (!GetWindowRect(handle, out rectangle)) throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        else
        {
            var placement = ReadPlacement(handle);
            rectangle = placement.NormalPosition;
            // WINDOWPLACEMENT normal bounds use workspace coordinates, unlike GetWindowRect.
            var info = ReadMonitor(MonitorFromRect(ref rectangle, 2));
            rectangle.Offset(info.Work.Left - info.Bounds.Left, info.Work.Top - info.Bounds.Top);
        }
        var monitor = Describe(MonitorFromRect(ref rectangle, 2));
        return new(rectangle.Left / monitor.DpiScale, rectangle.Top / monitor.DpiScale,
            (rectangle.Right - rectangle.Left) / monitor.DpiScale, (rectangle.Bottom - rectangle.Top) / monitor.DpiScale,
            maximized, folderTreeWidth, monitor.DeviceName,
            (rectangle.Left - monitor.Left) / monitor.DpiScale, (rectangle.Top - monitor.Top) / monitor.DpiScale);
    }

    public void Apply(Window window, WindowPlacement placement, bool beforeFirstShow)
    {
        var handle = new WindowInteropHelper(window).Handle;
        var rectangle = new NativeRect
        {
            Left = (int)Math.Round(placement.PixelLeft), Top = (int)Math.Round(placement.PixelTop),
            Right = (int)Math.Round(placement.PixelLeft + placement.Width * placement.Monitor.DpiScale),
            Bottom = (int)Math.Round(placement.PixelTop + placement.Height * placement.Monitor.DpiScale)
        };
        var monitor = ReadMonitor(MonitorFromRect(ref rectangle, 2));
        rectangle.Offset(monitor.Bounds.Left - monitor.Work.Left, monitor.Bounds.Top - monitor.Work.Top);
        var native = ReadPlacement(handle);
        native.NormalPosition = rectangle;
        if (beforeFirstShow) native.ShowCommand = 0; // Set restore bounds without briefly showing the normal window.
        else if (window.WindowState == WindowState.Normal) native.ShowCommand = 4; // SW_SHOWNOACTIVATE
        if (!SetWindowPlacement(handle, ref native)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static NativePlacement ReadPlacement(nint window)
    {
        var value = new NativePlacement { Length = Marshal.SizeOf<NativePlacement>() };
        if (!GetWindowPlacement(window, ref value)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return value;
    }

    private static NativeMonitor ReadMonitor(nint monitor)
    {
        var info = new NativeMonitor { Size = Marshal.SizeOf<NativeMonitor>(), DeviceName = string.Empty };
        if (!GetMonitorInfo(monitor, ref info)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return info;
    }

    private static MonitorWorkArea Describe(nint handle)
    {
        var info = ReadMonitor(handle);
        var dpi = GetDpiForMonitor(handle, 0, out var x, out _) == 0 && x > 0 ? x : GetDpiForSystem();
        return new(info.DeviceName, info.Work.Left, info.Work.Top, info.Work.Right - info.Work.Left,
            info.Work.Bottom - info.Work.Top, Math.Max(96, dpi) / 96d, (info.Flags & 1) != 0);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public void Offset(int x, int y) { Left += x; Right += x; Top += y; Bottom += y; }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativePlacement
    {
        public int Length, Flags, ShowCommand;
        public NativePoint Minimum, Maximum;
        public NativeRect NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeMonitor
    {
        public int Size;
        public NativeRect Bounds, Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref NativeRect rectangle, nint data);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetMonitorInfo(nint monitor, ref NativeMonitor info);
    [DllImport("user32.dll")] private static extern nint MonitorFromRect(ref NativeRect rectangle, uint flags);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(nint window, out NativeRect rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowPlacement(nint window, ref NativePlacement placement);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPlacement(nint window, ref NativePlacement placement);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
}
