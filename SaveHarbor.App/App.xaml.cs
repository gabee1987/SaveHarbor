using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
using SaveHarbor.App.ViewModels;
using Serilog;

namespace SaveHarbor.App;

public partial class App : Application
{
    private readonly IHost _host;
    private readonly IAppDataPathProvider _pathProvider = new AppDataPathProvider();

    public App()
    {
        ConfigureLogging(_pathProvider);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_pathProvider);
                services.AddSingleton<IAppLogger, SerilogAppLogger>();
                services.AddSingleton<IAppErrorHandler, AppErrorHandler>();
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

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        await _host.StartAsync();
        Log.ForContext("Keyword", "App").Information("SaveHarbor started");

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (mainWindow.DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        Log.ForContext("Keyword", "App").Information("SaveHarbor exiting");
        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging(IAppDataPathProvider pathProvider)
    {
        Directory.CreateDirectory(pathProvider.LocalLogsPath);
        Directory.CreateDirectory(pathProvider.CloudLogsPath);

        const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Keyword}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(pathProvider.LocalLogsPath, "saveharbor-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: outputTemplate)
            .WriteTo.File(
                Path.Combine(pathProvider.CloudLogsPath, "saveharbor-cloud-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                outputTemplate: outputTemplate)
            .CreateLogger();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.ForContext("Keyword", "App").Fatal(e.Exception, "Unhandled UI exception");
        e.Handled = false;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            Log.ForContext("Keyword", "App").Fatal(exception, "Unhandled application exception");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.ForContext("Keyword", "App").Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
