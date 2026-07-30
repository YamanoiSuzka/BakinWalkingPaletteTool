using System.Collections.ObjectModel;
using System.ComponentModel;

namespace BakinWalkingPaletteTool.Models;

public sealed class CharacterGroup : INotifyPropertyChanged
{
    private bool _isExpanded;

    public event PropertyChangedEventHandler? PropertyChanged;

    public required string CharacterName { get; init; }

    public ObservableCollection<SpriteFile> Files { get; } = [];

    public int FileCount => Files.Count;

    public string DisplayName =>
        Files.Count == 1 && !Files[0].IsAnimationFile
            ? $"{CharacterName}（単体）"
            : CharacterName;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value)
            {
                return;
            }

            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
}
