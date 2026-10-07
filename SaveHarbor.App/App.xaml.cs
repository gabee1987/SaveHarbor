using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.App.Infrastructure.Games.Windrose;
using SaveHarbor.App.Infrastructure.Migrations;
using SaveHarbor.App.Services;
using SaveHarbor.App.ViewModels;
using Serilog;
using Serilog.Events;

namespace SaveHarbor.App;

public partial class App : Application
{
    private readonly IHost _host;
    private readonly IAppDataPathProvider _pathProvider = new AppDataPathProvider();
    private readonly AppLoggingOptions _loggingOptions;
    private readonly CloudProviderOptions _cloudProviderOptions;

    public App()
    {
        _loggingOptions = AppOptionsLoader.LoadLoggingOptions();
        _cloudProviderOptions = AppOptionsLoader.LoadCloudProviderOptions();
        ConfigureLogging(_pathProvider, _loggingOptions);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_pathProvider);
                services.AddSingleton(_loggingOptions);
                services.AddSingleton(_cloudProviderOptions);
                services.AddSingleton<IAppLogger, SerilogAppLogger>();
                services.AddSingleton<IAppErrorHandler, AppErrorHandler>();
                services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
                services.AddSingleton<IPlayerIdentity, PlayerIdentity>();
                services.AddSingleton(serviceProvider => new GameOptionsProvider(
                    AppOptionsLoader.LoadGameOptions(),
                    serviceProvider.GetRequiredService<IAppSettingsStore>()));
                services.AddSingleton<WindroseSaveAdapter>();
                services.AddSingleton<IGameDefinition, WindroseGameDefinition>();
                services.AddSingleton<DragonwildsSaveAdapter>();
                services.AddSingleton<IGameDefinition, DragonwildsGameDefinition>();
                services.AddSingleton<IGameRegistry, GameRegistry>();
                services.AddSingleton<IActiveGameContext>(serviceProvider => new ActiveGameContext(
                    serviceProvider.GetRequiredService<IGameRegistry>(),
                    serviceProvider.GetRequiredService<IAppSettingsStore>(),
                    AppOptionsLoader.ReadGameArgument(Environment.GetCommandLineArgs())));
                services.AddSingleton<IBackupService, ZipBackupService>();
                services.AddSingleton<IProcessDetectionService, WindowsProcessDetectionService>();
                services.AddSingleton<IGameLauncherService, SteamGameLauncherService>();
                services.AddSingleton<IThemeService, ThemeService>();
                services.AddSingleton<IDialogService, WpfDialogService>();
                services.AddSingleton<IToastService, ToastService>();
                services.AddSingleton<ILocalSyncStateService, LocalJsonSyncStateService>();
                services.AddSingleton<FolderCloudProvider>();
                services.AddSingleton<GoogleDriveCloudProvider>();
                services.AddSingleton<ICloudProvider>(serviceProvider =>
                {
                    var options = serviceProvider.GetRequiredService<CloudProviderOptions>();
                    return string.Equals(options.Provider, CloudProviderKind.LocalTest, StringComparison.OrdinalIgnoreCase)
                        ? serviceProvider.GetRequiredService<FolderCloudProvider>()
                        : serviceProvider.GetRequiredService<GoogleDriveCloudProvider>();
                });
                services.AddSingleton<ICloudSetupService, CloudProviderSettingsService>();
                services.AddSingleton<ICloudSyncService, CloudSyncService>();
                services.AddSingleton<LegacyLayoutMigrator>();
                services.AddSingleton<BackupFolderOrganizer>();
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
        var migrationReport = _host.Services.GetRequiredService<LegacyLayoutMigrator>().Run();
        var organizerReport = _host.Services.GetRequiredService<BackupFolderOrganizer>()
            .Run(_host.Services.GetRequiredService<IGameRegistry>().All.Select(game => game.Id));
        LogAppInformation(_loggingOptions, "SaveHarbor started");

        _host.Services.GetRequiredService<IThemeService>();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (migrationReport.Errors.Count > 0 || organizerReport.Errors.Count > 0)
        {
            _host.Services.GetRequiredService<IToastService>().Warning(
                "Data migration incomplete",
                "Some older files could not be moved. Nothing was deleted. See the log.");
        }

        if (mainWindow.DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        LogAppInformation(_loggingOptions, "SaveHarbor exiting");
        await _host.StopAsync();
        _host.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void ConfigureLogging(IAppDataPathProvider pathProvider, AppLoggingOptions options)
    {
        Directory.CreateDirectory(pathProvider.LocalLogsPath);
        Directory.CreateDirectory(pathProvider.CloudLogsPath);

        const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Game}] [{Keyword}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(GetLowestConfiguredLevel(options))
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Game", "-")
            .WriteTo.File(
                Path.Combine(pathProvider.LocalLogsPath, "saveharbor-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                outputTemplate: outputTemplate)
            .WriteTo.File(
                Path.Combine(pathProvider.CloudLogsPath, "saveharbor-cloud-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: options.RetainedFileCountLimit,
                outputTemplate: outputTemplate)
            .CreateLogger();
    }

    private static LogEventLevel GetLowestConfiguredLevel(AppLoggingOptions options)
    {
        if (options.KeywordMinimumLevels.Count == 0)
        {
            return options.DefaultMinimumLevel;
        }

        return options.KeywordMinimumLevels.Values
            .Append(options.DefaultMinimumLevel)
            .Min();
    }

    private static void LogAppInformation(AppLoggingOptions options, string message)
    {
        if (options.IsEnabled(AppLogKeyword.App, LogEventLevel.Information))
        {
            Log.ForContext("Keyword", AppLogKeyword.App.ToString()).Information(message);
        }
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
