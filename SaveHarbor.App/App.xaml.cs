using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App;

public partial class App : Application
{
    private readonly IHost _host;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IWindroseSaveDiscoveryService, WindroseSaveDiscoveryService>();
                services.AddSingleton<IBackupService, ZipBackupService>();
                services.AddSingleton<IProcessDetectionService, WindowsProcessDetectionService>();
                services.AddSingleton<IDialogService, WpfDialogService>();
                services.AddSingleton<IToastService, ToastService>();
                services.AddSingleton<ILocalSyncStateService, LocalJsonSyncStateService>();
                services.AddSingleton<ICloudProvider, FolderCloudProvider>();
                services.AddSingleton<ICloudSyncService, CloudSyncService>();
                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await _host.StartAsync();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (mainWindow.DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();
        base.OnExit(e);
    }
}
