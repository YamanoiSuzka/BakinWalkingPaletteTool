using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BakinWalkingPaletteTool.Models;
using BakinWalkingPaletteTool.ViewModels;

namespace BakinWalkingPaletteTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
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

    private void PreviewImage_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || PreviewImageControl.Source is not BitmapSource image
            || PreviewImageControl.ActualWidth <= 0
            || PreviewImageControl.ActualHeight <= 0)
        {
            return;
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
            return;
        }

        var pixelX = Math.Min(
            (int)((position.X - offsetX) / scale),
            image.PixelWidth - 1);
        var pixelY = Math.Min(
            (int)((position.Y - offsetY) / scale),
            image.PixelHeight - 1);

        viewModel.SelectPreviewPixel(pixelX, pixelY);
        e.Handled = true;
    }
}
