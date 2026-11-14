namespace WatchPointer.Core.Pointer;

/// <summary>
/// SendInput with MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK takes coordinates normalised to 0..65535 across the
/// virtual desktop. Windows maps a normalised value n back to pixel floor(n · size / 65536). We pick the middle of the
/// interval of n values that land on the wanted pixel, so rounding can't push the cursor one pixel off.
/// </summary>
public static class AbsoluteMouseCoordinates
{
    public static int Normalize(int pixel, int origin, int size)
    {
        if (size <= 0) return 0;
        var p = Math.Clamp(pixel - origin, 0, size - 1);
        var n = ((2L * p + 1) * 65536) / (2L * size);
        return (int)Math.Clamp(n, 0, 65535);
    }

    /// <summary>What Windows does with a normalised coordinate (used to verify <see cref="Normalize"/>).</summary>
    public static int Denormalize(int normalized, int origin, int size) => origin + (int)((long)normalized * size / 65536);
}
