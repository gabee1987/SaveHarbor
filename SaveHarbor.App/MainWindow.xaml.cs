using System.Windows;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App;

public partial class MainWindow : Window
{
    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
