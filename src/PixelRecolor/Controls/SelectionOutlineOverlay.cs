using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelRecolor.Models;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WindowsPoint = System.Windows.Point;

namespace PixelRecolor.Controls;

/// <summary>
/// プレビュー画像内で、選択色が使われている領域の外周を点線で表示します。
/// </summary>
public sealed class SelectionOutlineOverlay : FrameworkElement
{
    public static readonly DependencyProperty SourceProperty =
        DependencyProperty.Register(
            nameof(Source),
            typeof(BitmapSource),
            typeof(SelectionOutlineOverlay),
            new FrameworkPropertyMetadata(null, OnSourceChanged));

    public static readonly DependencyProperty PaletteColorsProperty =
        DependencyProperty.Register(
            nameof(PaletteColors),
            typeof(ObservableCollection<PaletteColor>),
            typeof(SelectionOutlineOverlay),
            new FrameworkPropertyMetadata(null, OnPaletteColorsChanged));

    private uint[]? _pixels;
    private BitmapSource? _cachedSource;

    public BitmapSource? Source
    {
        get => (BitmapSource?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public ObservableCollection<PaletteColor>? PaletteColors
    {
        get => (ObservableCollection<PaletteColor>?)GetValue(PaletteColorsProperty);
        set => SetValue(PaletteColorsProperty, value);
    }

    /// <summary>
    /// RenderTransformによる現在の拡大率です。点線の太さを一定に保つために使います。
    /// </summary>
    public double Zoom { get; set; } = 1;

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (Source is not { PixelWidth: > 0, PixelHeight: > 0 } source
            || PaletteColors is null
            || ActualWidth <= 0
            || ActualHeight <= 0)
        {
            return;
        }

        var selectedColors = PaletteColors
            .Where(color => color.IsSelected)
            .Select(color => color.ArgbKey)
            .ToHashSet();
        if (selectedColors.Count == 0)
        {
            return;
        }

        EnsurePixelCache(source);
        if (_pixels is null)
        {
            return;
        }

        var scale = Math.Min(
            ActualWidth / source.PixelWidth,
            ActualHeight / source.PixelHeight);
        var offsetX = (ActualWidth - source.PixelWidth * scale) / 2;
        var offsetY = (ActualHeight - source.PixelHeight * scale) / 2;
        var geometry = CreateOutlineGeometry(
            source.PixelWidth,
            source.PixelHeight,
            selectedColors,
            scale,
            offsetX,
            offsetY);

        // 黒い下線と明るい点線を重ね、明色・暗色のどちらでも輪郭を見やすくします。
        var zoom = Math.Max(Zoom, 0.01);
        var backgroundPen = new MediaPen(MediaBrushes.Black, 3 / zoom);
        var dottedPen = new MediaPen(MediaBrushes.White, 1.5 / zoom)
        {
            DashStyle = new DashStyle([2.0, 2.0], 0)
        };
        backgroundPen.Freeze();
        dottedPen.Freeze();

        drawingContext.DrawGeometry(null, backgroundPen, geometry);
        drawingContext.DrawGeometry(null, dottedPen, geometry);
    }

    public void RefreshZoom(double zoom)
    {
        Zoom = zoom;
        InvalidateVisual();
    }

    private void EnsurePixelCache(BitmapSource source)
    {
        if (ReferenceEquals(source, _cachedSource) && _pixels is not null)
        {
            return;
        }

        var converted = new FormatConvertedBitmap(
            source,
            PixelFormats.Bgra32,
            null,
            0);
        var stride = checked(converted.PixelWidth * 4);
        var bytes = new byte[checked(stride * converted.PixelHeight)];
        converted.CopyPixels(bytes, stride, 0);

        _pixels = new uint[checked(converted.PixelWidth * converted.PixelHeight)];
        for (var pixelIndex = 0; pixelIndex < _pixels.Length; pixelIndex++)
        {
            var byteIndex = pixelIndex * 4;
            _pixels[pixelIndex] = ((uint)bytes[byteIndex + 3] << 24)
                | ((uint)bytes[byteIndex + 2] << 16)
                | ((uint)bytes[byteIndex + 1] << 8)
                | bytes[byteIndex];
        }

        _cachedSource = source;
    }

    private StreamGeometry CreateOutlineGeometry(
        int width,
        int height,
        IReadOnlySet<uint> selectedColors,
        double scale,
        double offsetX,
        double offsetY)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!IsSelected(x, y, width, selectedColors))
                {
                    continue;
                }

                var left = offsetX + x * scale;
                var top = offsetY + y * scale;
                var right = left + scale;
                var bottom = top + scale;

                if (x == 0 || !IsSelected(x - 1, y, width, selectedColors))
                {
                    AddLine(context, left, top, left, bottom);
                }

                if (x == width - 1 || !IsSelected(x + 1, y, width, selectedColors))
                {
                    AddLine(context, right, top, right, bottom);
                }

                if (y == 0 || !IsSelected(x, y - 1, width, selectedColors))
                {
                    AddLine(context, left, top, right, top);
                }

                if (y == height - 1 || !IsSelected(x, y + 1, width, selectedColors))
                {
                    AddLine(context, left, bottom, right, bottom);
                }
            }
        }

        geometry.Freeze();
        return geometry;
    }

    private bool IsSelected(
        int x,
        int y,
        int width,
        IReadOnlySet<uint> selectedColors)
    {
        return _pixels is not null
            && selectedColors.Contains(_pixels[y * width + x]);
    }

    private static void AddLine(
        StreamGeometryContext context,
        double x1,
        double y1,
        double x2,
        double y2)
    {
        context.BeginFigure(new WindowsPoint(x1, y1), false, false);
        context.LineTo(new WindowsPoint(x2, y2), true, false);
    }

    private static void OnSourceChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var overlay = (SelectionOutlineOverlay)dependencyObject;
        overlay._cachedSource = null;
        overlay._pixels = null;
        overlay.InvalidateVisual();
    }

    private static void OnPaletteColorsChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs e)
    {
        var overlay = (SelectionOutlineOverlay)dependencyObject;
        overlay.Unsubscribe((ObservableCollection<PaletteColor>?)e.OldValue);
        overlay.Subscribe((ObservableCollection<PaletteColor>?)e.NewValue);
        overlay.InvalidateVisual();
    }

    private void Subscribe(ObservableCollection<PaletteColor>? colors)
    {
        if (colors is null)
        {
            return;
        }

        colors.CollectionChanged += PaletteColors_CollectionChanged;
        foreach (var color in colors)
        {
            color.PropertyChanged += PaletteColor_PropertyChanged;
        }
    }

    private void Unsubscribe(ObservableCollection<PaletteColor>? colors)
    {
        if (colors is null)
        {
            return;
        }

        colors.CollectionChanged -= PaletteColors_CollectionChanged;
        foreach (var color in colors)
        {
            color.PropertyChanged -= PaletteColor_PropertyChanged;
        }
    }

    private void PaletteColors_CollectionChanged(
        object? sender,
        NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (PaletteColor color in e.OldItems)
            {
                color.PropertyChanged -= PaletteColor_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (PaletteColor color in e.NewItems)
            {
                color.PropertyChanged += PaletteColor_PropertyChanged;
            }
        }

        InvalidateVisual();
    }

    private void PaletteColor_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaletteColor.IsSelected))
        {
            InvalidateVisual();
        }
    }
}
