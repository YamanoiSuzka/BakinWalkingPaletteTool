using MediaColor = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using System.ComponentModel;

namespace BakinWalkingPaletteTool.Models;

public sealed class PaletteColor : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required MediaColor Color { get; init; }

    public required int PixelCount { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            PropertyChanged?.Invoke(
                this,
                new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public uint ArgbKey => ((uint)Color.A << 24)
        | ((uint)Color.R << 16)
        | ((uint)Color.G << 8)
        | Color.B;

    // 透明度を含むことが常に分かるよう、#RRGGBBAA形式の8桁で統一します。
    public string HexCode =>
        $"#{Color.R:X2}{Color.G:X2}{Color.B:X2}{Color.A:X2}";

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
