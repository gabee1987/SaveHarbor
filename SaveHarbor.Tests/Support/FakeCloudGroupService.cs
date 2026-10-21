using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure.Cloud.Supabase;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

// Sign-in and groups without a server: "123456" is the right code, TESTCODE22 the only invite code.
public sealed class FakeCloudGroupService : ICloudGroupService
{
    public const string RightCode = "123456";
    public const string InviteCode = "TESTCODE22";

    private readonly Dictionary<GameId, string> activeGroups = [];

    public bool IsSignedIn { get; set; }

    public string? SignedInEmail { get; set; }

    public List<CloudGroupSummary> ServerGroups { get; } = [];

    public IReadOnlyList<CloudGroupSummary> KnownGroups { get; set; } = [];

    public List<string> Calls { get; } = [];

    public string? LastJoinCode { get; private set; }

    public Task<CloudAuthResult> RequestSignInCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        Calls.Add("request");
        return Task.FromResult(email.Contains('@')
            ? new CloudAuthResult(true, $"We sent a 6-digit code to {email}.", NeedsCode: true)
            : new CloudAuthResult(false, "Enter a valid e-mail address."));
    }

    public Task<CloudAuthResult> VerifySignInCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        Calls.Add("verify");
        if (code != RightCode)
        {
            return Task.FromResult(new CloudAuthResult(false, "This code is wrong or has expired. Request a new code.", NeedsCode: true));
        }

        IsSignedIn = true;
        SignedInEmail = email;
        return Task.FromResult(new CloudAuthResult(true, $"Signed in as {email}."));
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("signout");
        IsSignedIn = false;
        SignedInEmail = null;
        activeGroups.Clear();
        KnownGroups = [];
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<CloudGroupSummary>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        Calls.Add("list");
        KnownGroups = ServerGroups.ToArray();
        return Task.FromResult(KnownGroups);
    }

    public Task<CloudGroupSummary> CreateGroupAsync(string name, string displayName, CancellationToken cancellationToken = default)
    {
        Calls.Add("create");
        var group = new CloudGroupSummary("12111111-1111-4111-8111-111111111111", name.Trim(), "owner", 1, DateTimeOffset.UnixEpoch);
        ServerGroups.Add(group);
        return Task.FromResult(group);
    }

    public Task<CloudGroupSummary> JoinGroupAsync(string inviteCode, string displayName, CancellationToken cancellationToken = default)
    {
        Calls.Add("join");
        LastJoinCode = SupabaseGroupService.NormalizeInviteCode(inviteCode);
        if (LastJoinCode != InviteCode)
        {
            throw new SupabaseException(SupabaseErrorCodes.InviteInvalid);
        }

        var group = new CloudGroupSummary(SupabaseHarness.GroupId, "TEST_GROUP", "member", 2, DateTimeOffset.UnixEpoch);
        ServerGroups.Add(group);
        return Task.FromResult(group);
    }

    public Task LeaveGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        Calls.Add("leave");
        return Task.CompletedTask;
    }

    public Task<string> GetInviteCodeAsync(string groupId, CancellationToken cancellationToken = default) => Task.FromResult(InviteCode);

    public Task<string> RotateInviteCodeAsync(string groupId, CancellationToken cancellationToken = default) => Task.FromResult("TESTCODE33");

    public string? GetActiveGroupId(GameId game) => activeGroups.GetValueOrDefault(game);

    public bool HasGroupConfigured(GameId game) => activeGroups.ContainsKey(game);

    public Task SetActiveGroupAsync(GameId game, string groupId, bool applyToAllGames, CancellationToken cancellationToken = default)
    {
        foreach (var target in applyToAllGames ? Enum.GetValues<GameId>() : [game])
        {
            activeGroups[target] = groupId;
        }

        return Task.CompletedTask;
    }
}
