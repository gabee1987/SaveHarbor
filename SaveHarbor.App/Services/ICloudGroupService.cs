using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

// Sign-in and groups for SaveHarbor Cloud, the counterpart of ICloudSetupService's shared folders.
public interface ICloudGroupService
{
    bool IsSignedIn { get; }

    string? SignedInEmail { get; }

    Task<CloudAuthResult> RequestSignInCodeAsync(string email, CancellationToken cancellationToken = default);

    Task<CloudAuthResult> VerifySignInCodeAsync(string email, string code, CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);

    // The groups from the last ListGroupsAsync, also after a restart.
    IReadOnlyList<CloudGroupSummary> KnownGroups { get; }

    Task<IReadOnlyList<CloudGroupSummary>> ListGroupsAsync(CancellationToken cancellationToken = default);

    Task<CloudGroupSummary> CreateGroupAsync(string name, string displayName, CancellationToken cancellationToken = default);

    Task<CloudGroupSummary> JoinGroupAsync(string inviteCode, string displayName, CancellationToken cancellationToken = default);

    Task LeaveGroupAsync(string groupId, CancellationToken cancellationToken = default);

    // Owners only.
    Task<string> GetInviteCodeAsync(string groupId, CancellationToken cancellationToken = default);

    Task<string> RotateInviteCodeAsync(string groupId, CancellationToken cancellationToken = default);

    string? GetActiveGroupId(GameId game);

    bool HasGroupConfigured(GameId game);

    Task SetActiveGroupAsync(GameId game, string groupId, bool applyToAllGames, CancellationToken cancellationToken = default);
}
