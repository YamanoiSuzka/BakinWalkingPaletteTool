using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BakinWalkingPaletteTool.Models;
using MediaColor = System.Windows.Media.Color;

namespace BakinWalkingPaletteTool.Services;

/// <summary>
/// PNGの読み込み、使用色の集計、色置換済みプレビューの生成を担当します。
/// </summary>
public sealed class ImageAnalysisService
{
    /// <summary>
    /// ファイルをロックし続けないよう、デコード結果をメモリへ読み切って返します。
    /// </summary>
    public BitmapSource LoadImage(string filePath)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);

        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);

        var image = decoder.Frames[0];
        image.Freeze();
        return image;
    }

    /// <summary>
    /// 画像内の不透明・半透明ピクセルをARGB単位で集計します。
    /// 完全透明なピクセルは背景領域としてパレットから除外します。
    /// </summary>
    public IReadOnlyList<PaletteColor> ExtractPalette(BitmapSource source)
    {
        // すべての入力形式を4バイト固定のBGRA32へ揃え、走査処理を単純化します。
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);

        var counts = new Dictionary<uint, int>();
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var blue = pixels[index];
            var green = pixels[index + 1];
            var red = pixels[index + 2];
            var alpha = pixels[index + 3];

            if (alpha == 0)
            {
                continue;
            }

            // Dictionaryのキーは 0xAARRGGBB の並びで保持します。
            var argb = ((uint)alpha << 24)
                | ((uint)red << 16)
                | ((uint)green << 8)
                | blue;

            counts[argb] = counts.GetValueOrDefault(argb) + 1;
        }

        return counts
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key)
            .Select(entry => new PaletteColor
            {
                Color = MediaColor.FromArgb(
                    (byte)(entry.Key >> 24),
                    (byte)(entry.Key >> 16),
                    (byte)(entry.Key >> 8),
                    (byte)entry.Key),
                PixelCount = entry.Value
            })
            .ToList();
    }

    /// <summary>
    /// 元色ARGBから置換後ARGBへの対応表を、画像の全ピクセルへ適用します。
    /// 入力画像は変更せず、新しいBitmapSourceを返します。
    /// </summary>
    public BitmapSource ApplyReplacements(
        BitmapSource source,
        IReadOnlyDictionary<uint, uint> replacements)
    {
        if (replacements.Count == 0)
        {
            return source;
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = checked(converted.PixelWidth * 4);
        var pixels = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);

        for (var index = 0; index < pixels.Length; index += 4)
        {
            // ピクセル配列はBGRA順のため、置換マップ用のARGBへ組み直します。
            var sourceArgb = ((uint)pixels[index + 3] << 24)
                | ((uint)pixels[index + 2] << 16)
                | ((uint)pixels[index + 1] << 8)
                | pixels[index];

            if (!replacements.TryGetValue(sourceArgb, out var targetArgb))
            {
                continue;
            }

            // ARGB値をWPFのBGRAピクセル配列へ戻します。
            pixels[index] = (byte)targetArgb;
            pixels[index + 1] = (byte)(targetArgb >> 8);
            pixels[index + 2] = (byte)(targetArgb >> 16);
            pixels[index + 3] = (byte)(targetArgb >> 24);
        }

        var result = BitmapSource.Create(
            converted.PixelWidth,
            converted.PixelHeight,
            converted.DpiX,
            converted.DpiY,
            PixelFormats.Bgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }
}
