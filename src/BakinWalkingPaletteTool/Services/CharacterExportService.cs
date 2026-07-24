using System.IO;
using System.Windows.Media.Imaging;
using BakinWalkingPaletteTool.Models;

namespace BakinWalkingPaletteTool.Services;

public sealed class CharacterExportService(ImageAnalysisService imageAnalysisService)
{
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

