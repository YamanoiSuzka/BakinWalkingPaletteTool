using System.Windows.Media;

namespace BakinWalkingPaletteTool.Models;

public sealed class PaletteColor
{
    public required Color Color { get; init; }

    public required int PixelCount { get; init; }

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

