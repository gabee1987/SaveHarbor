using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// The group the active game shares through: who is in it, how friends get in, and how much of the cloud it uses.
public sealed partial class SharingGroupViewModel : ObservableObject
{
    private readonly SharingServices _services;
    private readonly GameId _game;
    private readonly string _gameName;

    public SharingGroupViewModel(SharingServices services, GameId game, string gameName)
    {
        _services = services;
        _game = game;
        _gameName = gameName;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInviteCode))]
    private string inviteCode = string.Empty;

    [ObservableProperty]
    private string storageText = string.Empty;

    [ObservableProperty]
    private double storageFraction;

    [ObservableProperty]
    private bool hasStorageQuota;

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLoadError))]
    private string loadError = string.Empty;

    public ObservableCollection<SharingMember> Members { get; } = [];

    // The player's other groups, offered as a quick switch.
    public ObservableCollection<CloudGroupOption> OtherGroups { get; } = [];

    public string? GroupId => _services.Groups.GetActiveGroupId(_game);

    public CloudGroupSummary? Group => GroupId is { } id ? _services.Groups.KnownGroups.FirstOrDefault(group => group.Id == id) : null;

    public string GroupName => Group?.Name ?? "Your group";

    public string GroupInitial => SharingText.Initial(GroupName);

    public bool IsOwner => Group?.IsOwner == true;

    public bool HasInviteCode => InviteCode.Length > 0;

    public bool HasLoadError => LoadError.Length > 0;

    // Only the owner so far: the members card invites them to send the code.
    public bool IsAlone => IsOwner && Members.Count == 1;

    public bool HasOtherGroups => OtherGroups.Count > 0;

    public string OwnerName => Members.FirstOrDefault(member => member.IsOwner)?.Name ?? "the group owner";

    public string RoleLine
    {
        get
        {
            var role = IsOwner ? "You own this group" : "You are a member";
            var count = Members.Count > 0 ? Members.Count : Group?.MemberCount ?? 0;
            return count > 0 ? $"{role} · {SharingText.Members(count)}" : role;
        }
    }

    public string ScopeLine =>
        Enum.GetValues<GameId>().All(game => _services.Groups.GetActiveGroupId(game) == GroupId)
            ? "Shares worlds of both games"
            : $"Shares {_gameName} worlds";

    public string MemberInviteHint => $"Ask {OwnerName} for the invite code to bring more friends.";

    // Reads members, the invite code (owners only) and storage use; each part shows on its own when it arrives.
    public async Task LoadAsync()
    {
        if (GroupId is not { } groupId)
        {
            return;
        }

        IsLoading = true;
        LoadError = string.Empty;
        try
        {
            var groups = await _services.Groups.ListGroupsAsync();
            OtherGroups.Clear();
            foreach (var group in groups.Where(group => group.Id != groupId))
            {
                OtherGroups.Add(new CloudGroupOption(group));
            }

            NotifyGroup();
            await LoadMembersAsync();
            if (IsOwner && InviteCode.Length == 0)
            {
                InviteCode = await _services.Groups.GetInviteCodeAsync(groupId);
            }
        }
        catch (Exception exception)
        {
            LoadError = _services.ErrorHandler.Handle(exception, "Read your group", AppLogKeyword.CloudSync).UserMessage;
        }
        finally
        {
            IsLoading = false;
        }

        await LoadStorageAsync();
    }

    public async Task LoadMembersAsync()
    {
        if (GroupId is not { } groupId)
        {
            return;
        }

        var members = await _services.Groups.ListMembersAsync(groupId);
        var online = _services.Live is { State: CloudLiveState.Live } live
            ? live.Online.Select(entry => entry.UserId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];
        Members.Clear();
        foreach (var member in members)
        {
            Members.Add(new SharingMember(member, member.UserId.Length > 0 && online.Contains(member.UserId)));
        }

        NotifyGroup();
    }

    public async Task LoadStorageAsync()
    {
        try
        {
            if (await _services.Sync.GetStorageUsageAsync(_game) is not { } usage)
            {
                StorageText = string.Empty;
                return;
            }

            HasStorageQuota = usage.QuotaBytes is > 0;
            StorageFraction = usage.QuotaBytes is long quota && quota > 0 ? Math.Clamp((double)usage.UsedBytes / quota, 0, 1) : 0;
            StorageText = usage.QuotaBytes is { } total
                ? $"{DisplayFormatter.FormatBytes(usage.UsedBytes)} of {DisplayFormatter.FormatBytes(total)}"
                : $"{DisplayFormatter.FormatBytes(usage.UsedBytes)} used";
        }
        catch (Exception exception)
        {
            StorageText = _services.ErrorHandler.Handle(exception, "Read cloud storage use", AppLogKeyword.CloudSync).UserMessage;
        }
    }

    public void NotifyGroup()
    {
        OnPropertyChanged(nameof(GroupId));
        OnPropertyChanged(nameof(Group));
        OnPropertyChanged(nameof(GroupName));
        OnPropertyChanged(nameof(GroupInitial));
        OnPropertyChanged(nameof(IsOwner));
        OnPropertyChanged(nameof(RoleLine));
        OnPropertyChanged(nameof(ScopeLine));
        OnPropertyChanged(nameof(OwnerName));
        OnPropertyChanged(nameof(MemberInviteHint));
        OnPropertyChanged(nameof(HasOtherGroups));
        OnPropertyChanged(nameof(IsAlone));
    }

    public void Reset()
    {
        InviteCode = string.Empty;
        StorageText = string.Empty;
        StorageFraction = 0;
        Members.Clear();
        OtherGroups.Clear();
        NotifyGroup();
    }
}
