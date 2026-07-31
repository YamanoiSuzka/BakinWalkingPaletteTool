using System.ComponentModel;
using PixelRecolor.ViewModels;

namespace PixelRecolor;

public partial class ColorAdjustmentDialog : System.Windows.Window
{
    public ColorAdjustmentDialog()
    {
        InitializeComponent();
        Loaded += ColorAdjustmentDialog_Loaded;
        Closing += ColorAdjustmentDialog_Closing;
        Closed += ColorAdjustmentDialog_Closed;
    }

    private void ColorAdjustmentDialog_Loaded(
        object sender,
        System.Windows.RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ColorAdjustmentApplied += ViewModel_ColorAdjustmentApplied;
            viewModel.BeginColorAdjustmentPreview();
        }
    }

    private void ViewModel_ColorAdjustmentApplied(object? sender, EventArgs e)
    {
        Close();
    }

    private void ColorAdjustmentDialog_Closing(
        object? sender,
        CancelEventArgs e)
    {
        if (DataContext is not MainViewModel
            {
                HasPendingColorAdjustments: true
            })
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            "色調整がまだ適用されていません。\n\n"
            + "このまま閉じると、プレビュー中の調整は破棄されます。"
            + "\n閉じてもよろしいですか？",
            "未適用の色調整",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            e.Cancel = true;
        }
    }

    private void ColorAdjustmentDialog_Closed(
        object? sender,
        EventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
        {
            viewModel.ColorAdjustmentApplied -= ViewModel_ColorAdjustmentApplied;
            viewModel.EndColorAdjustmentPreview();
        }
    }
}
