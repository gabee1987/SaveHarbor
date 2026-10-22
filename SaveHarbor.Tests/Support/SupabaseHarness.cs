using System.Net.Http;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;

namespace SaveHarbor.Tests.Support;

// The SaveHarbor Cloud classes wired together over a scripted HTTP handler, with placeholder ids and a manual clock.
public sealed class SupabaseHarness : IDisposable
{
    public const string GroupId = "11111111-1111-4111-8111-111111111111";
    public const string WorldUuid = "22222222-2222-4222-8222-222222222222";
    public const string UserId = "44444444-4444-4444-8444-444444444444";
    public const string VersionId = "20261020_120000_TEST_USER_v1";
    public const string ObjectPath = GroupId + "/windrose/TEST_WORLD_ID/" + VersionId + ".zip";
    public const string AccessToken = "TEST_ACCESS_TOKEN";

    public SupabaseHarness(string url = "https://TEST_REF.supabase.co", Version? appVersion = null)
    {
        Paths = new TestPathProvider(Temp);
        Options = new SupabaseOptions { Url = url, PublishableKey = "sb_publishable_TEST" };
        Http = new SupabaseHttp(Options, new NullAppLogger(), Handler);
        SessionStore = new SupabaseSessionStore(Paths, new NullAppLogger());
        Auth = new SupabaseAuthClient(Http, SessionStore, new NullAppLogger(), Clock);
        OAuth = new SupabaseOAuthSignIn(Http, Options, Auth, Browser, new NullAppLogger());
        Rest = new SupabaseRest(Http, Auth);
        Storage = new SupabaseStorage(Http, Auth, Options);
        Settings = new CloudProviderSettingsService(new CloudProviderOptions(), new FakeCloudProvider(), Paths, new NullAppLogger());
        Groups = new SupabaseGroupService(Auth, OAuth, Rest, Settings, new NullAppLogger());
        Provider = new SupabaseCloudProvider(Options, Auth, Rest, Storage, Groups, new NullAppLogger(), Clock, appVersion ?? new Version(2, 3, 0));
    }

    public TempDirectory Temp { get; } = new();

    public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 20, 12, 30, 0, TimeSpan.Zero));

    public RecordingHttpMessageHandler Handler { get; } = new();

    public TestPathProvider Paths { get; }

    public SupabaseOptions Options { get; }

    public SupabaseHttp Http { get; }

    public SupabaseSessionStore SessionStore { get; }

    public SupabaseAuthClient Auth { get; }

    public RecordingBrowserLauncher Browser { get; } = new();

    public SupabaseOAuthSignIn OAuth { get; }

    public SupabaseRest Rest { get; }

    public SupabaseStorage Storage { get; }

    public CloudProviderSettingsService Settings { get; }

    public SupabaseGroupService Groups { get; }

    public SupabaseCloudProvider Provider { get; }

    public static GameWorld World { get; } =
        new(GameId.Windrose, "TEST_WORLD_ID", "Test World", string.Empty, "C:\\TEST_PATH", DateTimeOffset.MinValue, DateTimeOffset.MinValue, 0, 0);

    // A saved session valid for one more hour; the auth client loads it on first use.
    public void SignIn(TimeSpan? validFor = null) =>
        SessionStore.Save(new SupabaseSession(AccessToken, "TEST_REFRESH_TOKEN", Clock.Now + (validFor ?? TimeSpan.FromHours(1)), "test@example.invalid", UserId));

    public async Task SignInWithGroupAsync()
    {
        SignIn();
        await Settings.SetActiveGroupsAsync([GameId.Windrose], GroupId, TestContext.Current.CancellationToken);
        Handler.OnFixture(HttpMethod.Get, "rest/v1/app_meta", "app_meta.json");
    }

    public void Dispose()
    {
        Http.Dispose();
        Temp.Dispose();
    }
}
