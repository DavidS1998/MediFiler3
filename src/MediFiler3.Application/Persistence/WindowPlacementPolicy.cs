namespace MediFiler3.Application.Persistence;

// Monitor work areas use desktop pixels. Window sizes and monitor-relative offsets use DIPs.
public sealed record MonitorWorkArea(string DeviceName, double Left, double Top, double Width, double Height,
    double DpiScale, bool IsPrimary = false)
{
    public double LogicalWidth => Width / DpiScale;
    public double LogicalHeight => Height / DpiScale;
}

public sealed record WindowPlacement(MonitorWorkArea Monitor, double OffsetX, double OffsetY,
    double Width, double Height, bool Maximized, double FolderTreeWidth)
{
    public double PixelLeft => Monitor.Left + OffsetX * Monitor.DpiScale;
    public double PixelTop => Monitor.Top + OffsetY * Monitor.DpiScale;
    public WindowLayout ToLayout() => new(PixelLeft / Monitor.DpiScale, PixelTop / Monitor.DpiScale,
        Width, Height, Maximized, FolderTreeWidth, Monitor.DeviceName, OffsetX, OffsetY);
}

public static class WindowPlacementPolicy
{
    public static WindowPlacement Resolve(WindowLayout? saved, IReadOnlyList<MonitorWorkArea> monitors,
        double minimumWidth = 900, double minimumHeight = 600)
    {
        if (monitors.Count == 0) throw new ArgumentException("At least one monitor work area is required.", nameof(monitors));
        foreach (var monitor in monitors)
            if (!double.IsFinite(monitor.Left) || !double.IsFinite(monitor.Top) ||
                !double.IsFinite(monitor.Width) || monitor.Width <= 0 || !double.IsFinite(monitor.Height) || monitor.Height <= 0 ||
                !double.IsFinite(monitor.DpiScale) || monitor.DpiScale <= 0)
                throw new ArgumentException("Invalid monitor work area.", nameof(monitors));
        if (!double.IsFinite(minimumWidth) || minimumWidth <= 0 || !double.IsFinite(minimumHeight) || minimumHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(minimumWidth));
        try { saved?.Validate(); }
        catch (ArgumentException) { saved = null; }
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var match = saved?.MonitorDeviceName is { } name
            ? monitors.FirstOrDefault(m => string.Equals(m.DeviceName, name, StringComparison.OrdinalIgnoreCase)) : null;
        // Legacy records have absolute logical bounds only. Preserve the monitor with
        // the largest overlap; disconnected/entirely offscreen layouts use primary.
        var monitorChoice = match ?? (saved?.MonitorDeviceName is null && saved is not null
            ? monitors.OrderByDescending(m => Overlap(saved, m)).FirstOrDefault(m => Overlap(saved, m) > 0) : null) ?? primary;
        var width = Math.Clamp(saved?.Width ?? 1280, Math.Min(minimumWidth, monitorChoice.LogicalWidth), monitorChoice.LogicalWidth);
        var height = Math.Clamp(saved?.Height ?? 800, Math.Min(minimumHeight, monitorChoice.LogicalHeight), monitorChoice.LogicalHeight);
        var relative = match is not null && saved?.MonitorOffsetX is not null;
        var legacyOnMonitor = saved is not null && saved.MonitorDeviceName is null && Overlap(saved, monitorChoice) > 0;
        var x = relative ? saved!.MonitorOffsetX!.Value : legacyOnMonitor ? saved!.Left - monitorChoice.Left / monitorChoice.DpiScale : (monitorChoice.LogicalWidth - width) / 2;
        var y = relative ? saved!.MonitorOffsetY!.Value : legacyOnMonitor ? saved!.Top - monitorChoice.Top / monitorChoice.DpiScale : (monitorChoice.LogicalHeight - height) / 2;
        return new(monitorChoice, Math.Clamp(x, 0, monitorChoice.LogicalWidth - width),
            Math.Clamp(y, 0, monitorChoice.LogicalHeight - height), width, height, saved?.Maximized ?? false, saved?.FolderTreeWidth ?? 300);
    }

    private static double Overlap(WindowLayout saved, MonitorWorkArea monitor) =>
        Math.Max(0, Math.Min(saved.Left + saved.Width, (monitor.Left + monitor.Width) / monitor.DpiScale) - Math.Max(saved.Left, monitor.Left / monitor.DpiScale)) *
        Math.Max(0, Math.Min(saved.Top + saved.Height, (monitor.Top + monitor.Height) / monitor.DpiScale) - Math.Max(saved.Top, monitor.Top / monitor.DpiScale));
}
