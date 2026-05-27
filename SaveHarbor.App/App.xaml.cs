using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
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

    public App()
    {
        _loggingOptions = LoadLoggingOptions();
        ConfigureLogging(_pathProvider, _loggingOptions);

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton(_pathProvider);
                services.AddSingleton(_loggingOptions);
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
        LogAppInformation(_loggingOptions, "SaveHarbor started");

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

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

    private static AppLoggingOptions LoadLoggingOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .Build();

        var defaults = new AppLoggingOptions();
        var section = configuration.GetSection(AppLoggingOptions.SectionName);
        if (!section.Exists())
        {
            return defaults;
        }

        var options = new AppLoggingOptions
        {
            Enabled = ReadBool(section[nameof(AppLoggingOptions.Enabled)], defaults.Enabled),
            DefaultMinimumLevel = ReadLevel(
                section[nameof(AppLoggingOptions.DefaultMinimumLevel)],
                defaults.DefaultMinimumLevel),
            RetainedFileCountLimit = ReadInt(
                section[nameof(AppLoggingOptions.RetainedFileCountLimit)],
                defaults.RetainedFileCountLimit),
            KeywordMinimumLevels = new Dictionary<AppLogKeyword, LogEventLevel>(defaults.KeywordMinimumLevels)
        };

        foreach (var keywordSection in section.GetSection(nameof(AppLoggingOptions.KeywordMinimumLevels)).GetChildren())
        {
            if (Enum.TryParse<AppLogKeyword>(keywordSection.Key, ignoreCase: true, out var keyword))
            {
                options.KeywordMinimumLevels[keyword] = ReadLevel(
                    keywordSection.Value,
                    options.KeywordMinimumLevels.GetValueOrDefault(keyword, options.DefaultMinimumLevel));
            }
        }

        return options;
    }

    private static LogEventLevel ReadLevel(string? value, LogEventLevel fallback)
    {
        return Enum.TryParse<LogEventLevel>(value, ignoreCase: true, out var level)
            ? level
            : fallback;
    }

    private static bool ReadBool(string? value, bool fallback)
    {
        return bool.TryParse(value, out var result) ? result : fallback;
    }

    private static int ReadInt(string? value, int fallback)
    {
        return int.TryParse(value, out var result) && result > 0 ? result : fallback;
    }

    private static void ConfigureLogging(IAppDataPathProvider pathProvider, AppLoggingOptions options)
    {
        Directory.CreateDirectory(pathProvider.LocalLogsPath);
        Directory.CreateDirectory(pathProvider.CloudLogsPath);

        const string outputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{Keyword}] {Message:lj}{NewLine}{Exception}";

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(GetLowestConfiguredLevel(options))
            .Enrich.FromLogContext()
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
