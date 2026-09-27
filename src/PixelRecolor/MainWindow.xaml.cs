using System.ComponentModel;
using System.IO;
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
    private bool _isPreviewPanning;
    private System.Windows.Point _previewPanLastPosition;

    public MainWindow()
    {
        InitializeComponent();
        var viewModel = new MainViewModel();
        viewModel.PropertyChanged += MainViewModel_PropertyChanged;
        DataContext = viewModel;
    }

    private void MainWindow_PreviewDragOver(
        object sender,
        System.Windows.DragEventArgs e)
    {
        var paths = GetDroppedPaths(e.Data);
        e.Effects = paths.Length == 1 && IsSupportedDropPath(paths[0])
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void MainWindow_Drop(
        object sender,
        System.Windows.DragEventArgs e)
    {
        var paths = GetDroppedPaths(e.Data);
        e.Handled = true;

        if (paths.Length != 1)
        {
            System.Windows.MessageBox.Show(
                "PNGファイルまたはフォルダーを1つだけドロップしてください。",
                "ドラッグ＆ドロップ",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var path = paths[0];
        if (Directory.Exists(path))
        {
            viewModel.LoadFolder(path);
            return;
        }

        if (File.Exists(path)
            && string.Equals(
                Path.GetExtension(path),
                ".png",
                StringComparison.OrdinalIgnoreCase))
        {
            viewModel.LoadFile(path);
            return;
        }

        System.Windows.MessageBox.Show(
            "読み込めるのはPNGファイルまたはフォルダーです。",
            "対応していない項目です",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string[] GetDroppedPaths(System.Windows.IDataObject data)
    {
        return data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            && data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths
                ? paths
                : [];
    }

    private static bool IsSupportedDropPath(string path)
    {
        return Directory.Exists(path)
            || File.Exists(path)
            && string.Equals(
                Path.GetExtension(path),
                ".png",
                StringComparison.OrdinalIgnoreCase);
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

        // カーソル直下にある変換前の画像座標と、その現在の表示位置を求めます。
        // 拡大率を変更した後、同じ画像座標が同じ表示位置へ来るようOffsetを
        // 再計算することで、連続して拡大しても基準点がずれないようにします。
        var zoomCenter = e.GetPosition(PreviewTransformContainer);
        var matrix = PreviewMatrixTransform.Matrix;
        var displayedZoomCenter = matrix.Transform(zoomCenter);

        matrix.M11 *= actualFactor;
        matrix.M12 *= actualFactor;
        matrix.M21 *= actualFactor;
        matrix.M22 *= actualFactor;
        matrix.OffsetX = displayedZoomCenter.X
            - zoomCenter.X * matrix.M11
            - zoomCenter.Y * matrix.M21;
        matrix.OffsetY = displayedZoomCenter.Y
            - zoomCenter.X * matrix.M12
            - zoomCenter.Y * matrix.M22;
        PreviewMatrixTransform.Matrix = matrix;

        _previewZoom = nextZoom;
        if (Math.Abs(_previewZoom - 1) < 0.0001)
        {
            // 拡大表示を終えたときは、パンで動かした位置も中央へ戻します。
            PreviewMatrixTransform.Matrix = Matrix.Identity;
        }

        SelectionOutlineControl.RefreshZoom(_previewZoom);
        PreviewZoomTextBlock.Text = $"{_previewZoom * 100:F0}%";
        e.Handled = true;
    }

    private void PreviewViewport_PreviewMouseDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Middle
            || _previewZoom <= 1
            || PreviewImageControl.Source is null)
        {
            return;
        }

        _isPreviewPanning = PreviewViewport.CaptureMouse();
        if (!_isPreviewPanning)
        {
            return;
        }

        _previewPanLastPosition = e.GetPosition(PreviewViewport);
        PreviewViewport.Cursor = System.Windows.Input.Cursors.SizeAll;
        e.Handled = true;
    }

    private void PreviewViewport_PreviewMouseMove(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        if (!_isPreviewPanning)
        {
            return;
        }

        if (e.MiddleButton != MouseButtonState.Pressed)
        {
            EndPreviewPan();
            return;
        }

        var currentPosition = e.GetPosition(PreviewViewport);
        var offset = currentPosition - _previewPanLastPosition;
        var matrix = PreviewMatrixTransform.Matrix;

        // Offsetへ画面座標の移動量を加え、拡大率に関係なく
        // マウスと同じ距離だけプレビューを動かします。
        matrix.OffsetX += offset.X;
        matrix.OffsetY += offset.Y;
        PreviewMatrixTransform.Matrix = matrix;
        _previewPanLastPosition = currentPosition;
        e.Handled = true;
    }

    private void PreviewViewport_PreviewMouseUp(
        object sender,
        MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Middle && _isPreviewPanning)
        {
            EndPreviewPan();
            e.Handled = true;
        }
    }

    private void PreviewViewport_LostMouseCapture(
        object sender,
        System.Windows.Input.MouseEventArgs e)
    {
        _isPreviewPanning = false;
        PreviewViewport.Cursor = null;
    }

    private void EndPreviewPan()
    {
        _isPreviewPanning = false;
        PreviewViewport.Cursor = null;
        if (PreviewViewport.IsMouseCaptured)
        {
            PreviewViewport.ReleaseMouseCapture();
        }
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

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel
            && !viewModel.ConfirmDiscardUnsavedChanges("アプリを終了する"))
        {
            e.Cancel = true;
        }
    }

    private void ResetPreviewZoom()
    {
        EndPreviewPan();
        _previewZoom = 1;
        SelectionOutlineControl.RefreshZoom(_previewZoom);
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
