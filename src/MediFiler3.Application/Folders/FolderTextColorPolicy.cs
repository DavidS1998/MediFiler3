namespace MediFiler3.Application.Folders;

/// <summary>Framework-independent folder text rules; presentation only converts tones to brushes.</summary>
public static class FolderTextColorPolicy
{
    public static byte GetGray(int depth, bool depthShadingEnabled = true)
    {
        // Four equal intervals from white at depth 1 to half intensity (rounded to
        // the nearest byte) at depth 5. Roots share depth 1's white.
        return depthShadingEnabled
            ? (byte)Math.Round(255 - (255 - 128) * (Math.Clamp(depth, 1, 5) - 1) / 4d, MidpointRounding.AwayFromZero)
            : (byte)255;
    }
}
