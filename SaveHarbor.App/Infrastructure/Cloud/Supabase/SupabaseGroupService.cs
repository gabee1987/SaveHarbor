using System.Text.RegularExpressions;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// Sign-in and groups of SaveHarbor Cloud. The group chosen for each game and the last group list are kept in
// cloud-provider-settings.json; nothing about other members is stored on this PC.
public sealed partial class SupabaseGroupService : ICloudGroupService
{
    public const int InviteCodeLength = 10;

    private readonly SupabaseAuthClient auth;
    private readonly SupabaseRest rest;
    private readonly CloudProviderSettingsService settings;
    private readonly IAppLogger logger;
    private IReadOnlyList<CloudGroupSummary>? knownGroups;

    public SupabaseGroupService(SupabaseAuthClient auth, SupabaseRest rest, CloudProviderSettingsService settings, IAppLogger logger)
    {
        this.auth = auth;
        this.rest = rest;
        this.settings = settings;
        this.logger = logger;
    }

    [GeneratedRegex(@"^[^@\s]{1,64}@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailPattern();

    [GeneratedRegex("^[0-9]{6}$")]
    private static partial Regex CodePattern();

    public bool IsSignedIn => auth.IsSignedIn;

    public string? SignedInEmail => auth.Email;

    public IReadOnlyList<CloudGroupSummary> KnownGroups => knownGroups ??= settings.GetKnownGroups()
        .Select(group => new CloudGroupSummary(group.Id, group.Name, group.Role, 0, default))
        .ToArray();

    public async Task<CloudAuthResult> RequestSignInCodeAsync(string email, CancellationToken cancellationToken = default)
    {
        var address = email.Trim();
        if (address.Length > 254 || !EmailPattern().IsMatch(address))
        {
            return new CloudAuthResult(false, "Enter a valid e-mail address.");
        }

        try
        {
            await auth.RequestCodeAsync(address, cancellationToken);
            return new CloudAuthResult(true, $"We sent a 6-digit code to {address}. It is valid for 10 minutes.", NeedsCode: true);
        }
        catch (SupabaseException exception)
        {
            return new CloudAuthResult(false, SupabaseErrorMapper.Describe(exception).Message);
        }
    }

    public async Task<CloudAuthResult> VerifySignInCodeAsync(string email, string code, CancellationToken cancellationToken = default)
    {
        var digits = new string(code.Where(char.IsAsciiDigit).ToArray());
        if (!CodePattern().IsMatch(digits))
        {
            return new CloudAuthResult(false, "Enter the 6-digit code from the e-mail.", NeedsCode: true);
        }

        try
        {
            await auth.VerifyCodeAsync(email.Trim(), digits, cancellationToken);
            return new CloudAuthResult(true, $"Signed in as {auth.Email}.");
        }
        catch (SupabaseException exception)
        {
            // Auth answers a wrong code with 403 and a code it does not always name.
            var message = exception.Code is SupabaseErrorCodes.Forbidden or SupabaseErrorCodes.BadRequest
                ? SupabaseErrorMapper.Describe(new SupabaseException(SupabaseErrorCodes.OtpExpired)).Message
                : SupabaseErrorMapper.Describe(exception).Message;
            return new CloudAuthResult(false, message, NeedsCode: true);
        }
    }

    public async Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        await auth.SignOutAsync(cancellationToken);
        await settings.ClearGroupsAsync(cancellationToken);
        knownGroups = [];
    }

    public async Task<IReadOnlyList<CloudGroupSummary>> ListGroupsAsync(CancellationToken cancellationToken = default)
    {
        var groups = (await rest.ListGroupsAsync(cancellationToken)).Select(SupabaseMapping.ToGroup).ToArray();
        await RememberAsync(groups, cancellationToken);
        return groups;
    }

    public async Task<CloudGroupSummary> CreateGroupAsync(string name, string displayName, CancellationToken cancellationToken = default)
    {
        var group = SupabaseMapping.ToGroup(await rest.CreateGroupAsync(name.Trim(), PlayerIdentity.Normalize(displayName), cancellationToken));
        logger.Information(AppLogKeyword.CloudAuth, "Created a SaveHarbor Cloud group");
        await RememberAsync([.. KnownGroups.Where(known => known.Id != group.Id), group], cancellationToken);
        return group;
    }

    public async Task<CloudGroupSummary> JoinGroupAsync(string inviteCode, string displayName, CancellationToken cancellationToken = default)
    {
        var code = NormalizeInviteCode(inviteCode);
        if (code.Length != InviteCodeLength)
        {
            throw new SupabaseException(SupabaseErrorCodes.InviteInvalid);
        }

        var group = SupabaseMapping.ToGroup(await rest.JoinGroupAsync(code, PlayerIdentity.Normalize(displayName), cancellationToken));
        logger.Information(AppLogKeyword.CloudAuth, "Joined a SaveHarbor Cloud group");
        await RememberAsync([.. KnownGroups.Where(known => known.Id != group.Id), group], cancellationToken);
        return group;
    }

    public async Task LeaveGroupAsync(string groupId, CancellationToken cancellationToken = default)
    {
        await rest.LeaveGroupAsync(groupId, cancellationToken);
        logger.Information(AppLogKeyword.CloudAuth, "Left a SaveHarbor Cloud group");
        await RememberAsync(KnownGroups.Where(known => known.Id != groupId).ToArray(), cancellationToken);
    }

    public Task<string> GetInviteCodeAsync(string groupId, CancellationToken cancellationToken = default) =>
        rest.GetInviteCodeAsync(groupId, cancellationToken);

    public Task<string> RotateInviteCodeAsync(string groupId, CancellationToken cancellationToken = default) =>
        rest.RotateInviteCodeAsync(groupId, cancellationToken);

    public string? GetActiveGroupId(GameId game) => settings.GetActiveGroupId(game);

    public bool HasGroupConfigured(GameId game) => GetActiveGroupId(game) is not null;

    public async Task SetActiveGroupAsync(GameId game, string groupId, bool applyToAllGames, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(groupId, out _))
        {
            throw new ArgumentException("A group id is a UUID.", nameof(groupId));
        }

        await settings.SetActiveGroupsAsync(applyToAllGames ? Enum.GetValues<GameId>() : [game], groupId, cancellationToken);
    }

    // Upper case, and only the letters and digits invite codes use; "test code-22" becomes "TESTCODE22".
    public static string NormalizeInviteCode(string? inviteCode) =>
        new((inviteCode ?? string.Empty).ToUpperInvariant().Where(character => character is >= 'A' and <= 'Z' or >= '2' and <= '9').ToArray());

    private async Task RememberAsync(IReadOnlyList<CloudGroupSummary> groups, CancellationToken cancellationToken)
    {
        knownGroups = groups;
        await settings.SaveKnownGroupsAsync(
            groups.Select(group => new SavedCloudGroup { Id = group.Id, Name = group.Name, Role = group.MyRole }).ToArray(),
            cancellationToken);
    }
}
