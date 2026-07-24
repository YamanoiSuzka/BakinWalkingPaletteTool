using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace BakinWalkingPaletteTool.Models;

public sealed class PaletteColor
{
    public required MediaColor Color { get; init; }

    public required int PixelCount { get; init; }

    public uint ArgbKey => ((uint)Color.A << 24)
        | ((uint)Color.R << 16)
        | ((uint)Color.G << 8)
        | Color.B;

    public string HexCode => Color.A == byte.MaxValue
        ? $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}"
        : $"#{Color.A:X2}{Color.R:X2}{Color.G:X2}{Color.B:X2}";

    public SolidColorBrush Brush
    {
        get
        {
            var brush = new SolidColorBrush(Color);
            brush.Freeze();
            return brush;
        }
    }
}
