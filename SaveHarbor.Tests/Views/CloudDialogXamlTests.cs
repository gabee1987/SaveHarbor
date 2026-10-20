using System.Runtime.ExceptionServices;
using System.Windows;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
using SaveHarbor.App.ViewModels;
using SaveHarbor.App.Views.Dialogs;
using SaveHarbor.App.Views.Shell;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Views;

// The SaveHarbor Cloud dialogs and the changed shell views load with both skins: a missing static resource or a bad
// template only shows when the XAML is parsed, never at compile time.
public sealed class CloudDialogXamlTests
{
    private static readonly Uri[] Skins =
    [
        new("/SaveHarbor.App;component/Resources/Themes/Windrose/Windrose.Theme.xaml", UriKind.Relative),
        new("/SaveHarbor.App;component/Resources/Themes/Dragonwilds/Dragonwilds.Theme.xaml", UriKind.Relative)
    ];

    // WPF allows one Application per process; it lives on this test's own STA thread.
    private static void RunOnStaThread(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                if (Application.Current is null)
                {
                    var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SaveHarbor.App;component/Resources/Themes/Windrose.Colors.xaml", UriKind.Relative) });
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("/SaveHarbor.App;component/Resources/Styles/Controls.xaml", UriKind.Relative) });
                }

                action();
            }
            catch (Exception exception)
            {
                failure = ExceptionDispatchInfo.Capture(exception);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }

    [Fact]
    public void CloudDialogsAndViews_LoadWithEverySkin()
    {
        RunOnStaThread(() =>
        {
            foreach (var skin in Skins)
            {
                var choice = new CloudProviderChoiceWindow(skin, [new CloudProviderChoice(CloudProviderKind.Supabase, true), new CloudProviderChoice(CloudProviderKind.GoogleDrive, false)]);
                Layout(choice);
                choice.Close();

                var groups = new FakeCloudGroupService
                {
                    IsSignedIn = true,
                    SignedInEmail = "test@example.invalid",
                    KnownGroups = [new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "owner", 2, DateTimeOffset.UnixEpoch)]
                };
                var setup = new CloudGroupSetupViewModel(groups, new FixedPlayerIdentity(), new AppErrorHandler(new NullAppLogger()), GameId.Windrose, false);
                var window = new CloudGroupSetupWindow(skin, "Windrose", setup);
                Layout(window);
                window.Close();

                foreach (var view in new FrameworkElement[] { new ShellSharingView(), new ShellSettingsView() })
                {
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = skin });
                    view.Measure(new Size(900, 900));
                }
            }
        });
    }

    // Applies the templates (the group rows, the provider cards) without showing the window.
    private static void Layout(Window window)
    {
        var content = (FrameworkElement)window.Content;
        content.Measure(new Size(560, 1200));
        content.Arrange(new Rect(0, 0, 560, 1200));
    }
}
