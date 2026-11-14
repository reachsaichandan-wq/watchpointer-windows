namespace WatchPointer.Core.Pointer;

/// <summary>A point in virtual-desktop physical pixels (sub-pixel precision).</summary>
public readonly record struct PointD(double X, double Y)
{
    public static PointD operator +(PointD a, PointD b) => new(a.X + b.X, a.Y + b.Y);
    public static PointD operator -(PointD a, PointD b) => new(a.X - b.X, a.Y - b.Y);
    public static PointD operator *(PointD a, double k) => new(a.X * k, a.Y * k);
    public double ManhattanLength => Math.Abs(X) + Math.Abs(Y);
}

public readonly record struct PointI(int X, int Y);

/// <summary>Integer rectangle, right/bottom exclusive (like Win32 RECT). Physical pixels in virtual-desktop space.</summary>
public readonly record struct RectI(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public double CenterX => Left + Width / 2.0;
    public double CenterY => Top + Height / 2.0;
    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static RectI FromSize(int left, int top, int width, int height) => new(left, top, left + width, top + height);

    public bool Contains(PointD p) => p.X >= Left && p.X < Right && p.Y >= Top && p.Y < Bottom;

    /// <summary>Distance from <paramref name="p"/> to the nearest pixel of this rectangle (0 when inside).</summary>
    public double DistanceTo(PointD p)
    {
        var dx = Math.Max(Math.Max(Left - p.X, 0), p.X - (Right - 1));
        var dy = Math.Max(Math.Max(Top - p.Y, 0), p.Y - (Bottom - 1));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public PointD Clamp(PointD p) => new(Math.Clamp(p.X, Left, Right - 1), Math.Clamp(p.Y, Top, Bottom - 1));

    public RectI Union(RectI o) => new(Math.Min(Left, o.Left), Math.Min(Top, o.Top), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));

    public override string ToString() => $"{Width}×{Height} at ({Left}, {Top})";
}
