using SaveHarbor.App.Infrastructure;
using Velopack;

namespace SaveHarbor.App;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // The installer and updater start the app with hook arguments; Run() handles those and exits. A normal start
        // (also dotnet run) continues. Updates install only on "Restart now" or when the app closes, never on start, so
        // a version the player skipped after it was downloaded is not installed anyway. Invite links are registered for
        // the installed app and removed with it.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .OnAfterInstallFastCallback(_ => ProtocolRegistration.Register())
            .OnAfterUpdateFastCallback(_ => ProtocolRegistration.Register())
            .OnBeforeUninstallFastCallback(_ => ProtocolRegistration.Unregister())
            .Run();

        var appDataRoot = new AppDataPathProvider().AppDataRoot;
        using var guard = SingleInstanceGuard.TryAcquire(appDataRoot);
        if (guard is null)
        {
            SingleInstanceGuard.SignalRunningInstance(appDataRoot, args);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        guard.Activated += activationArgs => app.Dispatcher.BeginInvoke(() => app.Activate(activationArgs));
        app.Run();
    }
}
