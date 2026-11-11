namespace MediFiler3.Application.Navigation;

/// <summary>Separates an ordinary press/release from a drag; does not consume selection input.</summary>
public sealed class GalleryDragGesture
{
    private double _x, _y;
    private bool _armed, _started;
    public void Begin(double x, double y) { _x = x; _y = y; _armed = true; _started = false; }
    public void Reset() { _armed = _started = false; }
    public bool TryStart(double x, double y, bool leftButtonDown, double horizontalThreshold, double verticalThreshold)
    {
        if (!leftButtonDown) { Reset(); return false; }
        if (!_armed || _started || Math.Abs(x - _x) < horizontalThreshold && Math.Abs(y - _y) < verticalThreshold) return false;
        _started = true;
        return true;
    }
}
