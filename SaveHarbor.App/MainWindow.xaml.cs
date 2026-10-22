using System.ComponentModel;
using System.Windows;
using SaveHarbor.App.ViewModels;
using SaveHarbor.App.Views.Behaviors;

namespace SaveHarbor.App;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel viewModel;
    private TrayIcon? trayIcon;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = this.viewModel = viewModel;

        // While the game runs from Play, the window waits in the notification area.
        viewModel.MinimizeToTrayRequested += (_, _) => HideToTray();
        viewModel.RestoreFromTrayRequested += (_, _) => BringToFront();
        viewModel.TrayBalloonRequested += (_, balloon) => trayIcon?.Balloon(balloon.Title, balloon.Text);
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Closed += (_, _) => trayIcon?.Dispose();
    }

    // Also used when SaveHarbor is started a second time: the running window comes forward instead.
    public void BringToFront()
    {
        trayIcon?.Hide();
        if (!IsVisible)
        {
            Show();
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        viewModel.IsInTray = false;
        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    private void HideToTray()
    {
        if (trayIcon is null)
        {
            trayIcon = new TrayIcon(this);
            trayIcon.OpenRequested += (_, _) => BringToFront();
            trayIcon.ExitRequested += (_, _) => Close();
        }

        trayIcon.Show(viewModel.TrayTooltip);
        viewModel.IsInTray = true;
        Hide();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.TrayTooltip) && viewModel.IsInTray)
        {
            trayIcon?.Show(viewModel.TrayTooltip);
        }
    }
}
