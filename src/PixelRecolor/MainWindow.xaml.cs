using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelRecolor.Models;
using PixelRecolor.ViewModels;

namespace PixelRecolor;

public partial class MainWindow : Window
{
    private const double MinimumPreviewZoom = 0.25;
    private const double MaximumPreviewZoom = 16;
    private const double PreviewZoomStep = 1.2;
    private double _previewZoom = 1;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        viewModel.PropertyChanged += MainViewModel_PropertyChanged;
        DataContext = viewModel;
    }

    private void PaletteButton_PreviewMouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PaletteColor color }
            && DataContext is MainViewModel viewModel)
        {
            viewModel.TogglePaletteColorSelection(color);
            e.Handled = true;
        }
    }

    private void OpenColorAdjustmentDialog_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || !viewModel.HasSelectedColors)
        {
            return;
        }

        var dialog = new ColorAdjustmentDialog
        {
            Owner = this,
            DataContext = viewModel
        };
        dialog.ShowDialog();
    }

    private void PreviewImage_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && TryGetPreviewPixel(e, out var pixelX, out var pixelY))
        {
            viewModel.OpenPreviewPixelColorPicker(pixelX, pixelY);
            e.Handled = true;
        }
    }

    private void PreviewImage_MouseRightButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && TryGetPreviewPixel(e, out var pixelX, out var pixelY))
        {
            viewModel.TogglePreviewPixelSelection(pixelX, pixelY);
            e.Handled = true;
        }
    }

    private void PreviewImage_PreviewMouseWheel(
        object sender,
        MouseWheelEventArgs e)
    {
        if (PreviewImageControl.Source is null)
        {
            return;
        }

        var requestedFactor = e.Delta > 0
            ? PreviewZoomStep
            : 1 / PreviewZoomStep;
        var nextZoom = Math.Clamp(
            _previewZoom * requestedFactor,
            MinimumPreviewZoom,
            MaximumPreviewZoom);
        var actualFactor = nextZoom / _previewZoom;

        if (Math.Abs(actualFactor - 1) < double.Epsilon)
        {
            e.Handled = true;
            return;
        }

        // GetPositionはRenderTransform適用前の画像座標を返します。
        // その点を中心に前置スケールすることで、カーソル下のドットを
        // できるだけ同じ画面位置に保ったまま拡大縮小します。
        var zoomCenter = e.GetPosition(PreviewImageControl);
        var matrix = PreviewMatrixTransform.Matrix;
        matrix.ScaleAtPrepend(
            actualFactor,
            actualFactor,
            zoomCenter.X,
            zoomCenter.Y);
        PreviewMatrixTransform.Matrix = matrix;

        _previewZoom = nextZoom;
        PreviewZoomTextBlock.Text = $"{_previewZoom * 100:F0}%";
        e.Handled = true;
    }

    private void MainViewModel_PropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SelectedSpriteFile))
        {
            ResetPreviewZoom();
        }
    }

    private void ResetPreviewZoom()
    {
        _previewZoom = 1;
        PreviewMatrixTransform.Matrix = Matrix.Identity;
        PreviewZoomTextBlock.Text = "100%";
    }

    private bool TryGetPreviewPixel(
        MouseButtonEventArgs e,
        out int pixelX,
        out int pixelY)
    {
        pixelX = 0;
        pixelY = 0;

        if (PreviewImageControl.Source is not BitmapSource image
            || PreviewImageControl.ActualWidth <= 0
            || PreviewImageControl.ActualHeight <= 0)
        {
            return false;
        }

        // Stretch="Uniform"で生じる上下または左右の余白を除いて、
        // コントロール座標を元画像のピクセル座標へ変換します。
        var scale = Math.Min(
            PreviewImageControl.ActualWidth / image.PixelWidth,
            PreviewImageControl.ActualHeight / image.PixelHeight);
        var renderedWidth = image.PixelWidth * scale;
        var renderedHeight = image.PixelHeight * scale;
        var offsetX = (PreviewImageControl.ActualWidth - renderedWidth) / 2;
        var offsetY = (PreviewImageControl.ActualHeight - renderedHeight) / 2;
        var position = e.GetPosition(PreviewImageControl);

        if (position.X < offsetX
            || position.X >= offsetX + renderedWidth
            || position.Y < offsetY
            || position.Y >= offsetY + renderedHeight)
        {
            return false;
        }

        pixelX = Math.Min(
            (int)((position.X - offsetX) / scale),
            image.PixelWidth - 1);
        pixelY = Math.Min(
            (int)((position.Y - offsetY) / scale),
            image.PixelHeight - 1);

        return true;
    }
}
