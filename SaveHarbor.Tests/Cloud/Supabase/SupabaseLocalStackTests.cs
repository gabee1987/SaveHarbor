using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.Tests.Support;

namespace SaveHarbor.Tests.Cloud.Supabase;

// The provider against the LOCAL Supabase stack (npm run db:start) with the seed users. Skipped unless
// SAVEHARBOR_SUPABASE_LOCAL_KEY holds the local publishable key (npx supabase status -o env); never points at the
// hosted project, because sign-in codes are read from the local Mailpit.
public sealed partial class SupabaseLocalStackTests : IDisposable
{
    private const string ApiUrl = "http://127.0.0.1:54321";
    private const string MailpitUrl = "http://127.0.0.1:54324";
    private const string Email = "test-b@example.invalid";

    private readonly TempDirectory temp = new();

    public void Dispose() => temp.Dispose();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"\b(\d{6})\b")]
    private static partial Regex CodePattern();

    [Fact]
    public async Task SignIn_Upload_Lock_Download_Remove()
    {
        var key = Environment.GetEnvironmentVariable("SAVEHARBOR_SUPABASE_LOCAL_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Assert.Skip("Set SAVEHARBOR_SUPABASE_LOCAL_KEY to run against the local Supabase stack.");
        }

        var paths = new TestPathProvider(temp);
        var logger = new NullAppLogger();
        var options = new SupabaseOptions { Url = ApiUrl, PublishableKey = key };
        using var http = new SupabaseHttp(options, logger);
        var auth = new SupabaseAuthClient(http, new SupabaseSessionStore(paths, logger), logger, TimeProvider.System);
        var rest = new SupabaseRest(http, auth);
        var settings = new CloudProviderSettingsService(new CloudProviderOptions(), new FakeCloudProvider(), paths, logger);
        var groups = new SupabaseGroupService(auth, rest, settings, logger);
        var provider = new SupabaseCloudProvider(options, auth, rest, new SupabaseStorage(http, auth, options), groups, logger, TimeProvider.System, new Version(2, 3, 0));

        await SignInAsync(groups);
        var group = (await groups.ListGroupsAsync(Token)).Single(candidate => candidate.Id == SupabaseHarness.GroupId);
        await groups.SetActiveGroupAsync(GameId.Windrose, group.Id, applyToAllGames: false, Token);
        Assert.True((await provider.GetConnectionStatusAsync(GameId.Windrose, Token)).IsConnected);

        var world = SupabaseHarness.World with { WorldId = $"TEST_WORLD_{Guid.NewGuid():N}"[..24], WorldName = "Local Stack World" };
        var archive = temp.Combine("upload.zip");
        File.WriteAllBytes(archive, RandomNumberGenerator.GetBytes(4096));
        var versionId = $"{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}_TEST_USER_v1";
        var upload = await provider.UploadVersionAsync(new CloudUploadRequest(world, archive, new CloudVersionMetadata
        {
            VersionNumber = 1,
            VersionId = versionId,
            ArchiveFileName = versionId + ".zip",
            ArchiveSha256 = await Sha256Async(archive),
            ArchiveSizeBytes = new FileInfo(archive).Length,
            UploaderMachine = "TEST_DEVICE",
            SourceWorldModifiedAtUtc = DateTimeOffset.UtcNow
        }, null), Token);
        Assert.True(upload.IsSuccess, upload.Message);

        try
        {
            var manifest = await provider.GetWorldManifestAsync(GameId.Windrose, world.WorldId, Token);
            Assert.Equal(1, manifest!.LatestVersion!.VersionNumber);

            // The new archive is in use (and too young to be called unused); the listing works as a member.
            Assert.True((await provider.GetStorageUsageAsync(GameId.Windrose, Token))!.UsedBytes >= 4096);
            Assert.DoesNotContain(
                (await provider.FindUnusedFilesAsync(GameId.Windrose, Token)).Paths,
                path => path.Contains(world.WorldId, StringComparison.Ordinal));

            var stale = await provider.UploadVersionAsync(new CloudUploadRequest(world, archive, new CloudVersionMetadata
            {
                VersionNumber = 1,
                VersionId = versionId + "_again",
                ArchiveFileName = versionId + "_again.zip",
                ArchiveSha256 = await Sha256Async(archive),
                ArchiveSizeBytes = new FileInfo(archive).Length
            }, null), Token);
            Assert.False(stale.IsSuccess);
            Assert.Single(await provider.ListStoredVersionsAsync(GameId.Windrose, world.WorldId, Token));

            var now = DateTimeOffset.UtcNow;
            var requested = new CloudSessionLock
            {
                LockId = Guid.NewGuid().ToString("D"),
                WorldId = world.WorldId,
                PlayerName = "TEST_USER",
                MachineName = "TEST_DEVICE",
                DeviceId = Guid.NewGuid().ToString("D"),
                StartedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(20),
                BasedOnVersionNumber = 1,
                BasedOnVersionId = versionId
            };
            var attempt = await provider.TryAcquireSessionLockAsync(GameId.Windrose, requested, Token);
            Assert.True(attempt.Acquired);
            Assert.NotNull(await provider.HeartbeatSessionLockAsync(GameId.Windrose, world.WorldId, requested.LockId, TimeSpan.FromMinutes(20), Token));
            Assert.Equal(requested.LockId, (await provider.GetSessionLockAsync(GameId.Windrose, world.WorldId, Token))!.LockId);
            await provider.ClearSessionLockAsync(GameId.Windrose, world.WorldId, requested.LockId, Token);
            Assert.Null(await provider.GetSessionLockAsync(GameId.Windrose, world.WorldId, Token));

            var target = temp.Combine("download", manifest.LatestVersion.ArchiveFileName);
            var download = await provider.DownloadVersionAsync(new CloudDownloadRequest(world, manifest.LatestVersion, target), Token);
            Assert.True(download.IsSuccess);
            Assert.Equal(manifest.LatestVersion.ArchiveSha256, await Sha256Async(target));
        }
        finally
        {
            var removal = await provider.RemoveWorldAsync(GameId.Windrose, world.WorldId, Token);
            Assert.True(removal.IsSuccess, removal.Message);
        }

        Assert.Null(await provider.GetWorldManifestAsync(GameId.Windrose, world.WorldId, Token));
        await groups.SignOutAsync(Token);
        Assert.False(groups.IsSignedIn);
    }

    // Auth sends one code per address every 30 seconds; a recent run of the smoke test may have used it.
    private static async Task SignInAsync(SupabaseGroupService groups)
    {
        using var mailpit = new HttpClient { BaseAddress = new Uri(MailpitUrl) };
        await mailpit.DeleteAsync("api/v1/messages", Token);

        var request = await groups.RequestSignInCodeAsync(Email, Token);
        if (!request.IsSuccess)
        {
            await Task.Delay(TimeSpan.FromSeconds(31), Token);
            request = await groups.RequestSignInCodeAsync(Email, Token);
        }

        Assert.True(request.IsSuccess, request.Message);

        string? code = null;
        for (var attempt = 0; attempt < 40 && code is null; attempt++)
        {
            await Task.Delay(250, Token);
            using var search = JsonDocument.Parse(await mailpit.GetStringAsync($"api/v1/search?query={Uri.EscapeDataString("to:" + Email)}", Token));
            var messages = search.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                using var message = JsonDocument.Parse(await mailpit.GetStringAsync($"api/v1/message/{messages[0].GetProperty("ID").GetString()}", Token));
                var match = CodePattern().Match(message.RootElement.GetProperty("Text").GetString() ?? string.Empty);
                code = match.Success ? match.Groups[1].Value : null;
            }
        }

        Assert.NotNull(code);
        var verify = await groups.VerifySignInCodeAsync(Email, code, Token);
        Assert.True(verify.IsSuccess, verify.Message);
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, Token));
    }
}
