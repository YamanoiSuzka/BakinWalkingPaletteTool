using PixelRecolor.ViewModels;

namespace PixelRecolor;

public partial class ColorAdjustmentDialog : System.Windows.Window
{
    public ColorAdjustmentDialog()
    {
        InitializeComponent();
        Loaded += ColorAdjustmentDialog_Loaded;
        Closed += ColorAdjustmentDialog_Closed;
    }

    private void ColorAdjustmentDialog_Loaded(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.BeginColorAdjustmentPreview();
        }
    }

    private void ColorAdjustmentDialog_Closed(
        object? sender,
        EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.EndColorAdjustmentPreview();
        }
    }
}
