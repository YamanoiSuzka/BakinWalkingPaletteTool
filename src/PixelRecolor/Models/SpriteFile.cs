using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace PixelRecolor.Models;

public sealed class SpriteFile : INotifyPropertyChanged
{
    private bool _isSelected;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required string CharacterName { get; init; }

    public required string AnimationName { get; init; }

    public required bool IsAnimationFile { get; init; }

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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
}
