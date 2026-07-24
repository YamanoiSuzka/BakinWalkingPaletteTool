using System.IO;
using System.Windows.Media.Imaging;
using BakinWalkingPaletteTool.Models;

namespace BakinWalkingPaletteTool.Services;

/// <summary>
/// 選択キャラクターに属する全アニメーションを、色置換済みPNGとして出力します。
/// </summary>
public sealed class CharacterExportService(ImageAnalysisService imageAnalysisService)
{
    /// <summary>
    /// キャラクター名だけを差し替え、アニメーション名を維持した出力パスを作ります。
    /// </summary>
    public IReadOnlyList<string> GetOutputPaths(
        CharacterGroup character,
        string newCharacterName,
        string outputFolder)
    {
        return character.Files
            .Select(file => Path.Combine(
                outputFolder,
                $"{newCharacterName}_{file.AnimationName}.png"))
            .ToList();
    }

    /// <summary>
    /// 全アニメーションへ同じ置換マップを適用し、PNG形式で保存します。
    /// </summary>
    public IReadOnlyList<string> Export(
        CharacterGroup character,
        string newCharacterName,
        string outputFolder,
        IReadOnlyDictionary<uint, uint> replacements)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(newCharacterName);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFolder);

        if (!Directory.Exists(outputFolder))
        {
            throw new DirectoryNotFoundException(
                $"出力フォルダーが見つかりません: {outputFolder}");
        }

        var outputPaths = GetOutputPaths(
            character,
            newCharacterName,
            outputFolder);

        // Windowsではファイル名の大文字小文字を区別しないため、
        // OrdinalIgnoreCaseで出力先の重複を事前に検出します。
        if (outputPaths.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != outputPaths.Count)
        {
            throw new InvalidOperationException(
                "同じアニメーション名のファイルが複数あるため保存できません。");
        }

        for (var index = 0; index < character.Files.Count; index++)
        {
            var sourceFile = character.Files[index];
            var outputPath = outputPaths[index];
            // プレビュー画像を使い回さず、各アニメーションの元PNGへ同じ変換を適用します。
            var originalImage = imageAnalysisService.LoadImage(sourceFile.FilePath);
            var convertedImage = imageAnalysisService.ApplyReplacements(
                originalImage,
                replacements);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(convertedImage));

            using var stream = new FileStream(
                outputPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None);
            encoder.Save(stream);
        }

        return outputPaths;
    }
}
