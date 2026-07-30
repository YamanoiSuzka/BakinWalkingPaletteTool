using System.IO;
using PixelRecolor.Models;

namespace PixelRecolor.Services;

/// <summary>
/// PNGファイルを読み込み、命名規則に合う素材だけをキャラクター単位にまとめます。
/// </summary>
public sealed class SpriteFileLoader
{
    /// <summary>
    /// 指定フォルダー直下にあるすべてのPNGを読み込みます。
    /// サブフォルダーは、意図しない素材の混入を避けるため検索しません。
    /// </summary>
    public IReadOnlyList<CharacterGroup> LoadFromFolder(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"フォルダーが見つかりません: {folderPath}");
        }

        return LoadFiles(
            Directory.EnumerateFiles(
                folderPath,
                "*",
                SearchOption.TopDirectoryOnly));
    }

    /// <summary>
    /// 指定した複数のパスからPNGを読み込み、命名規則に応じてグループ化します。
    /// 命名規則に合わないPNGは、他画像へ色変更を波及させない独立グループにします。
    /// </summary>
    public IReadOnlyList<CharacterGroup> LoadFiles(IEnumerable<string> filePaths)
    {
        var spriteFiles = filePaths
            .Select(TryParse)
            .OfType<SpriteFile>()
            .OrderBy(file => file.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.AnimationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var groups = spriteFiles
            .Where(file => file.IsAnimationFile)
            .GroupBy(file => file.CharacterName, StringComparer.OrdinalIgnoreCase)
            .Select(CreateCharacterGroup)
            .ToList();

        foreach (var standaloneFile in spriteFiles.Where(
            file => !file.IsAnimationFile))
        {
            var standaloneGroup = new CharacterGroup
            {
                CharacterName = standaloneFile.CharacterName
            };
            standaloneGroup.Files.Add(standaloneFile);
            groups.Add(standaloneGroup);
        }

        return groups
            .OrderBy(group => group.CharacterName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// PNGのファイル情報を作ります。
    /// 「キャラクター名_アニメーション名.png」に一致する場合は両要素を解析し、
    /// それ以外のPNGはファイル名全体を独立グループ名として扱います。
    /// PNG以外の場合だけnullを返します。
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

        var isAnimationFile =
            separatorIndex > 0
            && separatorIndex < nameWithoutExtension.Length - 1;

        if (!isAnimationFile)
        {
            return new SpriteFile
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                CharacterName = string.IsNullOrEmpty(nameWithoutExtension)
                    ? Path.GetFileName(filePath)
                    : nameWithoutExtension,
                AnimationName = string.Empty,
                IsAnimationFile = false
            };
        }

        return new SpriteFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            CharacterName = nameWithoutExtension[..separatorIndex],
            AnimationName = nameWithoutExtension[(separatorIndex + 1)..],
            IsAnimationFile = true
        };
    }

    private static CharacterGroup CreateCharacterGroup(
        IGrouping<string, SpriteFile> group)
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
    }
}
