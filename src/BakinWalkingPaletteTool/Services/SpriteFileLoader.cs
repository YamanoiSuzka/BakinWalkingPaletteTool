using System.IO;
using BakinWalkingPaletteTool.Models;

namespace BakinWalkingPaletteTool.Services;

/// <summary>
/// フォルダー内のファイル名を解析し、Bakin用素材をキャラクター単位にまとめます。
/// </summary>
public sealed class SpriteFileLoader
{
    /// <summary>
    /// 指定フォルダー直下にある有効なPNGだけを読み込みます。
    /// サブフォルダーは、意図しない素材の混入を避けるため検索しません。
    /// </summary>
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

    /// <summary>
    /// 「キャラクター名_アニメーション名.png」を解析します。
    /// 形式に一致しない場合は例外ではなくnullを返し、呼び出し側で無視できるようにします。
    /// </summary>
    public SpriteFile? TryParse(string filePath)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

        // 最後の「_」を区切りにすることで、キャラクター名に「_」を含められます。
        // 例: villager_red_wait.png → villager_red / wait
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
