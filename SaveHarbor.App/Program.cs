using Velopack;

namespace SaveHarbor.App;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        // The installer and updater start the app with hook arguments; Run() handles those and exits. A normal start
        // (also dotnet run) continues. Updates install only on "Restart now" or when the app closes, never on start, so
        // a version the player skipped after it was downloaded is not installed anyway.
        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
