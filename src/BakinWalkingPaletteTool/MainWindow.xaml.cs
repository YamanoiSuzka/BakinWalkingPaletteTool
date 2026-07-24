using System.Windows;
using BakinWalkingPaletteTool.ViewModels;

namespace BakinWalkingPaletteTool;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}
