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
    /// PNG選択で1ファイルだけ開く場合は、ファイル名全体を単体画像名として扱えます。
    /// 命名規則に合わないPNGは、他画像へ色変更を波及させない独立グループにします。
    /// </summary>
    public IReadOnlyList<CharacterGroup> LoadFiles(
        IEnumerable<string> filePaths,
        bool treatSingleFileAsStandalone = false)
    {
        var paths = filePaths.ToList();
        var parseAnimationName =
            !treatSingleFileAsStandalone || paths.Count != 1;

        var spriteFiles = paths
            .Select(path => TryParse(path, parseAnimationName))
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
    /// PNG以外とサムネイル用の「thumb.png」「*.thumb.png」はnullを返します。
    /// </summary>
    public SpriteFile? TryParse(
        string filePath,
        bool parseAnimationName = true)
    {
        if (!string.Equals(Path.GetExtension(filePath), ".png", StringComparison.OrdinalIgnoreCase)
            || IsThumbnailFile(filePath))
        {
            return null;
        }

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(filePath);

        // 最初の「_」を区切りにして、残りをすべてアニメーション名として扱います。
        // 例: villager_red_wait.png → villager / red_wait
        var separatorIndex = nameWithoutExtension.IndexOf('_');

        var isAnimationFile =
            parseAnimationName
            && separatorIndex > 0
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

    private static bool IsThumbnailFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        return fileName.Equals("thumb.png", StringComparison.OrdinalIgnoreCase)
            || fileName.EndsWith(".thumb.png", StringComparison.OrdinalIgnoreCase);
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
