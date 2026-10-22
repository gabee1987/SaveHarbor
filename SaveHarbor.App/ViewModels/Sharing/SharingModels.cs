using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.ViewModels;

// What the Sharing page shows: the provider choice, the SaveHarbor Cloud sign-in, the group it shares through, or
// the Google Drive folder.
public enum SharingScreen
{
    Welcome,
    SignIn,
    Group,
    Drive
}

public enum CloudOnboardingStep
{
    Email,
    Code,
    Name,
    Group,
    Created
}

// The group step first offers the two ways in; each then shows only its own field.
public enum GroupSetupMode
{
    Choose,
    Join,
    Create
}

public sealed record CloudGroupSetupResult(string GroupId, string GroupName);

// Everything the Sharing page works with. SetupSharedFolderAsync opens the Google Drive folder dialog.
public sealed record SharingServices(
    ICloudProviderSelector Selector,
    ICloudGroupService Groups,
    ICloudSyncService Sync,
    ICloudSetupService Folders,
    IDeviceIdentity Device,
    IAppSettingsStore Settings,
    IPlayerIdentity Player,
    IAppErrorHandler ErrorHandler,
    IDialogService Dialogs,
    IToastService Toasts,
    Func<Task> SetupSharedFolderAsync,
    ICloudLiveUpdates? Live = null);

// A group the player already belongs to. The member count is unknown (0) for groups remembered from an earlier start.
public sealed record CloudGroupOption(CloudGroupSummary Group)
{
    public string Name => Group.Name;

    public string Initial => SharingText.Initial(Group.Name);

    public string Detail => Group.MemberCount > 0
        ? $"{SharingText.Members(Group.MemberCount)} · {(Group.IsOwner ? "you own it" : "member")}"
        : Group.IsOwner ? "You own it" : "Member";
}

// One face in the group's member list.
// IsOnline: SaveHarbor is open for that member right now (SaveHarbor Cloud presence).
public sealed record SharingMember(CloudGroupMember Member, bool IsOnline = false)
{
    public string Name => Member.DisplayName;

    public string Initial => SharingText.Initial(Member.DisplayName);

    public bool IsOwner => Member.IsOwner;

    public bool IsYou => Member.IsYou;

    public string Detail => (Member.IsOwner, Member.IsYou) switch
    {
        (true, true) => "Owner · you",
        (true, false) => "Owner",
        (false, true) => "You",
        _ when IsOnline => "Online now",
        _ => $"Joined {Member.JoinedAtUtc.ToLocalTime():d MMM yyyy}"
    };
}

public static class SharingText
{
    // The first letter or digit, upper case; "?" for a name without one.
    public static string Initial(string? name) =>
        (name ?? string.Empty).FirstOrDefault(char.IsLetterOrDigit) is var first && first != default
            ? char.ToUpperInvariant(first).ToString()
            : "?";

    public static string Members(int count) => count == 1 ? "1 member" : $"{count} members";
}
