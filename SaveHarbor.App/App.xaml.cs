using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Cloud;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.App.Infrastructure.Games;
using SaveHarbor.App.Infrastructure.Games.Dragonwilds;
using SaveHarbor.App.Infrastructure.Games.Enshrouded;
using SaveHarbor.App.Infrastructure.Games.Windrose;
using SaveHarbor.App.Infrastructure.Migrations;
using SaveHarbor.App.Infrastructure.Updates;
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
    private readonly UpdateOptions _updateOptions;

    public App()
    {
        _loggingOptions = AppOptionsLoader.LoadLoggingOptions();
        _cloudProviderOptions = AppOptionsLoader.LoadCloudProviderOptions();
        _updateOptions = AppOptionsLoader.LoadUpdateOptions(Environment.GetCommandLineArgs());
        ConfigureLogging(_pathProvider, _loggingOptions);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_pathProvider);
                services.AddSingleton(_loggingOptions);
                services.AddSingleton(_cloudProviderOptions);
                services.AddSingleton(_cloudProviderOptions.Supabase);
                services.AddSingleton(TimeProvider.System);
                services.AddSingleton<IAppLogger, SerilogAppLogger>();
                services.AddSingleton<IAppErrorHandler, AppErrorHandler>();
                services.AddSingleton<IAppSettingsStore, AppSettingsStore>();
                services.AddSingleton<IPlayerIdentity, PlayerIdentity>();
                services.AddSingleton<IDeviceIdentity, DeviceIdentity>();
                services.AddSingleton(serviceProvider => new GameOptionsProvider(
                    AppOptionsLoader.LoadGameOptions(),
                    serviceProvider.GetRequiredService<IAppSettingsStore>()));
                services.AddSingleton<WindroseSaveAdapter>();
                services.AddSingleton<IGameDefinition, WindroseGameDefinition>();
                services.AddSingleton<DragonwildsSaveAdapter>();
                services.AddSingleton<IGameDefinition, DragonwildsGameDefinition>();
                services.AddSingleton<IEnshroudedSaveRootLocator, EnshroudedSaveRootLocator>();
                services.AddSingleton<EnshroudedSaveAdapter>();
                services.AddSingleton<IGameDefinition, EnshroudedGameDefinition>();
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
                services.AddSingleton(serviceProvider => new SupabaseHttp(
                    serviceProvider.GetRequiredService<SupabaseOptions>(),
                    serviceProvider.GetRequiredService<IAppLogger>()));
                services.AddSingleton<SupabaseSessionStore>();
                services.AddSingleton<SupabaseAuthClient>();
                services.AddSingleton<IBrowserLauncher, SystemBrowserLauncher>();
                services.AddSingleton<SupabaseOAuthSignIn>();
                services.AddSingleton<SupabaseRest>();
                services.AddSingleton<SupabaseStorage>();
                services.AddSingleton<SupabaseGroupService>();
                services.AddSingleton<ICloudGroupService>(serviceProvider => serviceProvider.GetRequiredService<SupabaseGroupService>());
                services.AddSingleton(serviceProvider => new SupabaseCloudProvider(
                    serviceProvider.GetRequiredService<SupabaseOptions>(),
                    serviceProvider.GetRequiredService<SupabaseAuthClient>(),
                    serviceProvider.GetRequiredService<SupabaseRest>(),
                    serviceProvider.GetRequiredService<SupabaseStorage>(),
                    serviceProvider.GetRequiredService<ICloudGroupService>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    serviceProvider.GetRequiredService<TimeProvider>()));
                services.AddSingleton(serviceProvider => new SupabaseRealtimeClient(
                    serviceProvider.GetRequiredService<SupabaseOptions>(),
                    serviceProvider.GetRequiredService<SupabaseAuthClient>(),
                    serviceProvider.GetRequiredService<IAppLogger>()));
                services.AddSingleton<ICloudLiveUpdates>(serviceProvider => serviceProvider.GetRequiredService<SupabaseRealtimeClient>());
                services.AddSingleton(serviceProvider => new CloudProviderSelector(
                    serviceProvider.GetRequiredService<CloudProviderOptions>(),
                    serviceProvider.GetRequiredService<IAppSettingsStore>(),
                    serviceProvider.GetRequiredService<FolderCloudProvider>(),
                    serviceProvider.GetRequiredService<GoogleDriveCloudProvider>(),
                    () => serviceProvider.GetRequiredService<SupabaseCloudProvider>(),
                    serviceProvider.GetRequiredService<IAppLogger>()));
                services.AddSingleton<ICloudProviderSelector>(serviceProvider => serviceProvider.GetRequiredService<CloudProviderSelector>());
                services.AddSingleton<ICloudProvider>(serviceProvider => serviceProvider.GetRequiredService<CloudProviderSelector>());
                services.AddSingleton<CloudProviderSettingsService>();
                services.AddSingleton<ICloudSetupService>(serviceProvider => serviceProvider.GetRequiredService<CloudProviderSettingsService>());
                services.AddSingleton<ICloudSyncService, CloudSyncService>();
                services.AddSingleton<CloudSnapshotCache>();
                services.AddSingleton<IAppUpdateService>(serviceProvider => new VelopackUpdateService(
                    _updateOptions,
                    serviceProvider.GetRequiredService<IAppSettingsStore>(),
                    serviceProvider.GetRequiredService<IProcessDetectionService>(),
                    serviceProvider.GetRequiredService<IActiveGameContext>(),
                    serviceProvider.GetRequiredService<IAppLogger>(),
                    VelopackUpdateService.CreateSourceFactory(_updateOptions)));
                services.AddSingleton<LegacyLayoutMigrator>();
                services.AddSingleton<SyncStateProviderScopeMigrator>();
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
        var games = _host.Services.GetRequiredService<IGameRegistry>().All.Select(game => game.Id).ToArray();
        var migrationReport = _host.Services.GetRequiredService<LegacyLayoutMigrator>().Run();
        var syncStateReport = _host.Services.GetRequiredService<SyncStateProviderScopeMigrator>().Run(games);
        var organizerReport = _host.Services.GetRequiredService<BackupFolderOrganizer>().Run(games);
        LogAppInformation(_loggingOptions, "SaveHarbor started");

        _host.Services.GetRequiredService<IThemeService>();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (migrationReport.Errors.Count > 0 || syncStateReport.Errors.Count > 0 || organizerReport.Errors.Count > 0)
        {
            _host.Services.GetRequiredService<IToastService>().Warning(
                "Data migration incomplete",
                "Some older files could not be moved. Nothing was deleted. See the log.");
        }

        await ApplyProviderArgumentAsync(e.Args);
        if (mainWindow.DataContext is MainWindowViewModel viewModel)
        {
            await viewModel.InitializeAsync();
            if (InviteLink.Find(e.Args) is not null)
            {
                await viewModel.HandleActivationAsync(e.Args);
            }
        }
    }

    // SaveHarbor was started again while running: the window comes forward and handles the new start's arguments.
    public async void Activate(IReadOnlyList<string> args)
    {
        try
        {
            if (MainWindow is not SaveHarbor.App.MainWindow window || window.DataContext is not MainWindowViewModel viewModel)
            {
                return;
            }

            window.BringToFront();
            await viewModel.HandleActivationAsync(args);
        }
        catch (Exception ex)
        {
            _host.Services.GetRequiredService<IAppErrorHandler>().Handle(ex, "Handle a second start", AppLogKeyword.Ui);
        }
    }

    // --provider supabase|googledrive, for shortcuts and support: makes that provider the active one.
    private async Task ApplyProviderArgumentAsync(IReadOnlyList<string> args)
    {
        var selector = _host.Services.GetRequiredService<ICloudProviderSelector>();
        if (AppOptionsLoader.ReadProviderArgument(args) is { } kind && selector.AvailableKinds.Contains(kind) && selector.ActiveKind != kind)
        {
            await selector.SwitchAsync(kind);
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
