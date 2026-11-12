namespace MediFiler3.Application.Navigation;

public enum ReaderScalingMode { FitWidth, FitHeight }

public sealed record ReaderOptions(ReaderScalingMode Scaling = ReaderScalingMode.FitWidth,
    int Spacing = 0, int Preload = 2, bool ShowPageIndicator = true, int WidthPercent = 100)
{
    public void Validate()
    {
        if (!Enum.IsDefined(Scaling)) throw new ArgumentOutOfRangeException(nameof(Scaling));
        if (Spacing is < 0 or > 64 || Spacing % 4 != 0) throw new ArgumentOutOfRangeException(nameof(Spacing));
        if (WidthPercent is < 10 or > 100) throw new ArgumentOutOfRangeException(nameof(WidthPercent));
        if (Preload is < 0 or > 6) throw new ArgumentOutOfRangeException(nameof(Preload));
    }
}
