using System.Runtime.ExceptionServices;
using System.Windows;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;
using SaveHarbor.App.ViewModels;
using SaveHarbor.App.Views.Dialogs;
using SaveHarbor.App.Views.Shell;
using SaveHarbor.App.Views.Sharing;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Views;

// The SaveHarbor Cloud and update dialogs and the changed shell views load with both skins: a missing static resource or a bad
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
            using var temp = new TempDirectory();
            foreach (var skin in Skins)
            {
                // The Sharing page at the width Settings gives it in the smallest main window.
                foreach (var sharing in SharingOnEveryScreen(temp))
                {
                    var view = new SharingView { DataContext = sharing };
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = skin });
                    Layout(view, 720);
                }

                foreach (var view in new FrameworkElement[] { new ShellSharingView(), new ShellSettingsView(), new GameShellView(), new ShellFooterView() })
                {
                    view.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = skin });
                    view.Measure(new Size(900, 900));
                }
            }
        });
    }

    [Fact]
    public void ReleaseNotesDialog_ShowsTheDocument()
    {
        RunOnStaThread(() =>
        {
            var window = new AppDialogWindow { DialogTitle = "What's new", Document = MarkdownLite.ToFlowDocument("## Added\n- Updates") };
            Layout((FrameworkElement)window.Content, 900);

            Assert.NotNull(window.Document);
            Assert.False(window.SecondaryChosen);
            window.Close();
        });
    }

    [Fact]
    public void PatchNotesWindow_LoadsWithEverySkin()
    {
        RunOnStaThread(() =>
        {
            Uri[] skins = [.. Skins, new("/SaveHarbor.App;component/Resources/Themes/Enshrouded/Enshrouded.Theme.xaml", UriKind.Relative)];
            foreach (var skin in skins)
            {
                var viewModel = new PatchNotesViewModel(PatchNotesCatalog.Releases, "2.3.0");
                var window = new PatchNotesWindow(skin) { DataContext = viewModel };
                Layout((FrameworkElement)window.Content, 980);

                Assert.Equal("2.3.0", viewModel.SelectedRelease?.Version);
                window.Close();
            }
        });
    }

    // One view model per screen and sign-in step, so every part of the Sharing page is parsed and templated.
    private static IEnumerable<SharingViewModel> SharingOnEveryScreen(TempDirectory temp)
    {
        var screens = new List<SharingViewModel>();
        foreach (var kind in new[] { string.Empty, CloudProviderKind.Supabase, CloudProviderKind.GoogleDrive })
        {
            var groups = new FakeCloudGroupService
            {
                IsSignedIn = kind == CloudProviderKind.Supabase,
                SignedInEmail = "test@example.invalid",
                KnownGroups = [new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "owner", 2, DateTimeOffset.UnixEpoch)]
            };
            groups.ServerGroups.AddRange(groups.KnownGroups);
            groups.Members.Add(new CloudGroupMember("TEST_USER", "owner", true, DateTimeOffset.UnixEpoch));
            groups.SetActiveGroupAsync(GameId.Windrose, SupabaseHarness.GroupId, true).GetAwaiter().GetResult();
            var settings = new AppSettingsStore(new TestPathProvider(temp));
            var sharing = new SharingViewModel(
                new SharingServices(new FixedProviderSelector(kind), groups, new StubCloudSyncService { Usage = new CloudStorageUsage(12345, 1073741824) },
                    new StubCloudSetupService(), new FixedDeviceIdentity(), settings, new PlayerIdentity(settings),
                    new AppErrorHandler(new NullAppLogger()), new RecordingDialogService(), new RecordingToastService(), () => Task.CompletedTask),
                GameId.Windrose,
                "Windrose");
            sharing.InitializeAsync().GetAwaiter().GetResult();
            screens.Add(sharing);
        }

        // The sign-in steps, each on its own.
        foreach (var step in Enum.GetValues<GroupSetupMode>())
        {
            var settings = new AppSettingsStore(new TestPathProvider(temp));
            var sharing = new SharingViewModel(
                new SharingServices(new FixedProviderSelector(CloudProviderKind.Supabase), new FakeCloudGroupService { IsSignedIn = true }, new StubCloudSyncService(),
                    new StubCloudSetupService(), new FixedDeviceIdentity(), settings, new PlayerIdentity(settings),
                    new AppErrorHandler(new NullAppLogger()), new RecordingDialogService(), new RecordingToastService(), () => Task.CompletedTask),
                GameId.Windrose,
                "Windrose");
            sharing.InitializeAsync().GetAwaiter().GetResult();
            sharing.Onboarding!.ShowGroupStep(step);
            screens.Add(sharing);
        }

        return screens;
    }

    // Applies the templates (the group rows, the provider cards) without showing a window.
    private static void Layout(FrameworkElement content, double width)
    {
        content.Measure(new Size(width, 1200));
        content.Arrange(new Rect(0, 0, width, 1200));
    }
}
