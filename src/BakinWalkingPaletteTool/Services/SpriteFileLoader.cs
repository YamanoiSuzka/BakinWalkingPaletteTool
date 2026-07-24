using System.IO;
using BakinWalkingPaletteTool.Models;

namespace BakinWalkingPaletteTool.Services;

public sealed class SpriteFileLoader
{
    public IReadOnlyList<CharacterGroup> LoadFromFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"フォルダーが見つかりません: {folderPath}");
        }

        var spriteFiles = Directory
            .EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .Select(TryParse)
            .OfType<SpriteFile>()
            .OrderBy(file => file.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.AnimationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase);

        return spriteFiles
            .GroupBy(file => file.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var characterGroup = new CharacterGroup
                {
                    CharacterName = group.Key
                };

                foreach (var file in group)
                {
                    characterGroup.Files.Add(file);
                }

                return characterGroup;
            })
            .ToList();
    }

    public SpriteFile? TryParse(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);
        var separatorIndex = nameWithoutExtension.LastIndexOf('_');

        if (separatorIndex <= 0 || separatorIndex >= nameWithoutExtension.Length - 1)
        {
            return null;
        }

        return new SpriteFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            CharacterName = nameWithoutExtension[..separatorIndex],
            AnimationName = nameWithoutExtension[(separatorIndex + 1)..]
        };
    }
}
