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

    /// <summary>
    /// 指定座標の色を 0xAARRGGBB 形式で取得します。
    /// プレビュー画像のクリック位置からパレット色を選ぶために使用します。
    /// </summary>
    public uint GetArgbAt(BitmapSource source, int x, int y)
    {
        if (x < 0 || x >= source.PixelWidth || y < 0 || y >= source.PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var pixel = new byte[4];
        converted.CopyPixels(
            new System.Windows.Int32Rect(x, y, 1, 1),
            pixel,
            4,
            0);

        return ((uint)pixel[3] << 24)
            | ((uint)pixel[2] << 16)
            | ((uint)pixel[1] << 8)
            | pixel[0];
    }

    /// <summary>
    /// ARGB色へHSVベースの相対調整を適用します。
    /// 透明度は正の値ほど透明になるよう、アルファ値とは逆方向に計算します。
    /// </summary>
    public uint AdjustArgb(
        uint argb,
        double hueShift,
        double saturationAdjustment,
        double brightnessAdjustment,
        double transparencyAdjustment)
    {
        var alpha = (byte)(argb >> 24);
        var red = (byte)(argb >> 16);
        var green = (byte)(argb >> 8);
        var blue = (byte)argb;

        RgbToHsv(red, green, blue, out var hue, out var saturation, out var value);
        hue = (hue + hueShift) % 360;
        if (hue < 0)
        {
            hue += 360;
        }

        saturation = Math.Clamp(
            saturation + saturationAdjustment / 100.0,
            0,
            1);
        value = Math.Clamp(
            value + brightnessAdjustment / 100.0,
            0,
            1);

        HsvToRgb(hue, saturation, value, out red, out green, out blue);
        alpha = (byte)Math.Round(Math.Clamp(
            alpha - transparencyAdjustment / 100.0 * byte.MaxValue,
            0,
            byte.MaxValue));

        return ((uint)alpha << 24)
            | ((uint)red << 16)
            | ((uint)green << 8)
            | blue;
    }

    private static void RgbToHsv(
        byte red,
        byte green,
        byte blue,
        out double hue,
        out double saturation,
        out double value)
    {
        var r = red / 255.0;
        var g = green / 255.0;
        var b = blue / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;

        hue = 0;
        if (delta > 0)
        {
            if (max == r)
            {
                hue = 60 * (((g - b) / delta) % 6);
            }
            else if (max == g)
            {
                hue = 60 * ((b - r) / delta + 2);
            }
            else
            {
                hue = 60 * ((r - g) / delta + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        saturation = max == 0 ? 0 : delta / max;
        value = max;
    }

    private static void HsvToRgb(
        double hue,
        double saturation,
        double value,
        out byte red,
        out byte green,
        out byte blue)
    {
        var chroma = value * saturation;
        var x = chroma * (1 - Math.Abs(hue / 60 % 2 - 1));
        var match = value - chroma;

        var (r, g, b) = hue switch
        {
            < 60 => (chroma, x, 0.0),
            < 120 => (x, chroma, 0.0),
            < 180 => (0.0, chroma, x),
            < 240 => (0.0, x, chroma),
            < 300 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x)
        };

        red = (byte)Math.Round((r + match) * byte.MaxValue);
        green = (byte)Math.Round((g + match) * byte.MaxValue);
        blue = (byte)Math.Round((b + match) * byte.MaxValue);
    }
}
