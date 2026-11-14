namespace WatchPointer.Core.Pointer;

/// <summary>One monitor. Bounds are physical pixels in the virtual desktop (the process is per-monitor DPI aware).</summary>
public sealed record DisplayInfo(string Id, string Name, RectI Bounds, RectI WorkArea, bool IsPrimary, double Scale);

/// <summary>Source of the current monitor layout (Win32 implementation lives in the platform project).</summary>
public interface IDisplayProvider
{
    DisplayLayout Current { get; }
    event Action<DisplayLayout>? Changed;
}

/// <summary>Immutable snapshot of all monitors, as arranged in Windows Settings › Display.</summary>
public sealed class DisplayLayout
{
    public DisplayLayout(IReadOnlyList<DisplayInfo> displays)
    {
        if (displays.Count == 0)
            displays = [new DisplayInfo("default", "Display", RectI.FromSize(0, 0, 1920, 1080), RectI.FromSize(0, 0, 1920, 1040), true, 1)];
        Displays = displays;
        Primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
        var v = displays[0].Bounds;
        foreach (var d in displays) v = v.Union(d.Bounds);
        VirtualBounds = v;
    }

    public IReadOnlyList<DisplayInfo> Displays { get; }
    public DisplayInfo Primary { get; }

    /// <summary>Bounding box of all monitors (may include gaps no monitor covers).</summary>
    public RectI VirtualBounds { get; }

    /// <summary>The display containing <paramref name="p"/>, or the nearest one when <paramref name="p"/> is off every screen.</summary>
    public DisplayInfo DisplayAt(PointD p)
    {
        DisplayInfo best = Displays[0];
        var bestDistance = double.MaxValue;
        foreach (var d in Displays)
        {
            var distance = d.Bounds.DistanceTo(p);
            if (distance < bestDistance) { best = d; bestDistance = distance; if (distance == 0) break; }
        }
        return best;
    }

    /// <summary><paramref name="p"/> if it is on a display, else the closest point on the nearest display
    /// (so gaps between differently sized screens are skipped and the cursor never leaves the desktop).</summary>
    public PointD Clamp(PointD p) => DisplayAt(p).Bounds.Clamp(p);

    /// <summary>The rectangle the absolute pointer is projected onto.</summary>
    public RectI PointingArea(PointingArea area, string? displayId)
    {
        return area switch
        {
            Pointer.PointingArea.AllDisplays => VirtualBounds,
            Pointer.PointingArea.SpecificDisplay => (Displays.FirstOrDefault(d => d.Id == displayId) ?? Primary).Bounds,
            _ => Primary.Bounds,
        };
    }

    public static DisplayLayout Single(int width, int height) =>
        new([new DisplayInfo("primary", "Primary", RectI.FromSize(0, 0, width, height), RectI.FromSize(0, 0, width, height), true, 1)]);
}

public enum PointingArea
{
    /// <summary>Original behaviour: aim is projected onto the primary display; pointing further reaches neighbouring displays.</summary>
    PrimaryDisplay,
    /// <summary>The whole virtual desktop is one big wall (sensitivity spreads across all monitors).</summary>
    AllDisplays,
    /// <summary>A chosen monitor is the wall; pointing past it still reaches neighbours.</summary>
    SpecificDisplay,
}
