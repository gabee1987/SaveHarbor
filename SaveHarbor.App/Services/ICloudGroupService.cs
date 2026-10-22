using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

// Sign-in and groups for SaveHarbor Cloud, the counterpart of ICloudSetupService's shared folders.
public interface ICloudGroupService
{
    bool IsSignedIn { get; }

    string? SignedInEmail { get; }

    Task<CloudAuthResult> RequestSignInCodeAsync(string email, CancellationToken cancellationToken = default);

    Task<CloudAuthResult> VerifySignInCodeAsync(string email, string code, CancellationToken cancellationToken = default);

    // False while the cloud cannot be reached or has Google sign-in switched off.
    Task<bool> IsGoogleSignInAvailableAsync(CancellationToken cancellationToken = default);

    // Opens Google sign-in in the browser and waits up to five minutes for the player to finish there.
    Task<CloudAuthResult> SignInWithGoogleAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    // The groups from the last ListGroupsAsync, also after a restart.
    IReadOnlyList<CloudGroupSummary> KnownGroups { get; }

    Task<IReadOnlyList<CloudGroupSummary>> ListGroupsAsync(CancellationToken cancellationToken = default);

    Task<CloudGroupSummary> CreateGroupAsync(string name, string displayName, CancellationToken cancellationToken = default);

    Task<CloudGroupSummary> JoinGroupAsync(string inviteCode, string displayName, CancellationToken cancellationToken = default);

    Task LeaveGroupAsync(string groupId, CancellationToken cancellationToken = default);

    // Owner first, then by joining date.
    Task<IReadOnlyList<CloudGroupMember>> ListMembersAsync(string groupId, CancellationToken cancellationToken = default);

    // The name the other members of this group see for the signed-in player.
    Task SetDisplayNameAsync(string groupId, string displayName, CancellationToken cancellationToken = default);

    // Owners only.
    Task<string> GetInviteCodeAsync(string groupId, CancellationToken cancellationToken = default);

    Task<string> RotateInviteCodeAsync(string groupId, CancellationToken cancellationToken = default);

    string? GetActiveGroupId(GameId game);

    bool HasGroupConfigured(GameId game);

    Task SetActiveGroupAsync(GameId game, string groupId, bool applyToAllGames, CancellationToken cancellationToken = default);
}
