using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using WatchPointer.App.ViewModels;

namespace WatchPointer.App;

/// <summary>Status colours chosen to read well on both the light and the dark Fluent theme.</summary>
public sealed class ToneToBrushConverter : IValueConverter
{
    private static readonly Brush Good = Frozen(0x2E, 0xA0, 0x43), Warning = Frozen(0xD8, 0x8C, 0x00), Bad = Frozen(0xD1, 0x34, 0x38), Neutral = Frozen(0x8A, 0x8A, 0x8A);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        StatusTone.Good => Good,
        StatusTone.Warning => Warning,
        StatusTone.Bad => Bad,
        _ => Neutral,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>0..1 fraction × the size given as parameter, minus half the marker (parameter "240,10" = size 240, marker 10).</summary>
public sealed class FractionToOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "100,0").Split(',');
        var size = double.Parse(parts[0], CultureInfo.InvariantCulture);
        var marker = parts.Length > 1 ? double.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
        return value is double f ? f * size - marker / 2 : 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value is true) ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class StepVisibilityConverter : IValueConverter
{
    /// <summary>Visible when the int value matches one of the comma-separated parameter values.</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int step && (parameter as string ?? "").Split(',').Contains(step.ToString(CultureInfo.InvariantCulture)) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class BoolToTextConverter : IValueConverter
{
    /// <summary>Parameter "TextIfTrue|TextIfFalse".</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var parts = (parameter as string ?? "Yes|No").Split('|');
        return value is true ? parts[0] : parts.ElementAtOrDefault(1) ?? "";
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
