using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

// Who this player is to friends, on both providers: the one name used on sessions, uploads and in every SaveHarbor Cloud
// group, this PC's name, and how many shared versions uploads keep. Every change is saved immediately.
public sealed partial class SharingProfileViewModel : ObservableObject
{
    private readonly SharingServices _services;

    public SharingProfileViewModel(SharingServices services)
    {
        _services = services;
    }

    public IReadOnlyList<RetentionOption> CloudRetentionOptions { get; } =
    [
        new("Keep every version", 0),
        new("Keep the newest 5", 5),
        new("Keep the newest 10", 10),
        new("Keep the newest 20", 20)
    ];

    // Shows the name friends actually see; clearing it goes back to the Windows user name.
    public string DisplayName
    {
        get => _services.Settings.Current.PlayerName is { Length: > 0 } name ? name : EffectiveDisplayName;
        set
        {
            var normalized = PlayerIdentity.Normalize(value);
            if (normalized == DisplayName)
            {
                return;
            }

            Save(normalized);
            _ = PushDisplayNameAsync();
        }
    }

    public string EffectiveDisplayName => _services.Player.DisplayName;

    public string Initial => SharingText.Initial(EffectiveDisplayName);

    // Shows this PC's name as friends see it; clearing it goes back to the Windows computer name.
    public string DeviceLabel
    {
        get => _services.Settings.Current.DeviceLabel is { Length: > 0 } label ? label : EffectiveDeviceLabel;
        set
        {
            var normalized = PlayerIdentity.Normalize(value);
            if (normalized == DeviceLabel)
            {
                return;
            }

            _services.Settings.Update(settings => settings.DeviceLabel = normalized);
            OnPropertyChanged();
            OnPropertyChanged(nameof(EffectiveDeviceLabel));
        }
    }

    public string EffectiveDeviceLabel => _services.Device.DeviceLabel;

    public string? Email => _services.Groups.IsSignedIn ? _services.Groups.SignedInEmail : null;

    public RetentionOption SelectedCloudRetention
    {
        get => CloudRetentionOptions.FirstOrDefault(option => option.Count == _services.Settings.Current.CloudVersionRetentionCount) ?? CloudRetentionOptions[0];
        set
        {
            if (value is null || value.Count == _services.Settings.Current.CloudVersionRetentionCount)
            {
                return;
            }

            _services.Settings.Update(settings => settings.CloudVersionRetentionCount = value.Count);
            OnPropertyChanged();
        }
    }

    public bool UploadWhenGameCloses
    {
        get => _services.Settings.Current.UploadWhenGameCloses;
        set
        {
            if (value == _services.Settings.Current.UploadWhenGameCloses)
            {
                return;
            }

            _services.Settings.Update(settings => settings.UploadWhenGameCloses = value);
            OnPropertyChanged();
        }
    }

    // SaveHarbor Cloud only; reaches the group when Settings closes.
    public bool ShowOnlineStatus
    {
        get => _services.Settings.Current.ShowOnlineStatus;
        set
        {
            if (value == _services.Settings.Current.ShowOnlineStatus)
            {
                return;
            }

            _services.Settings.Update(settings => settings.ShowOnlineStatus = value);
            OnPropertyChanged();
        }
    }

    // The name chosen during sign-in; the groups get it when they are joined or created.
    public void Save(string displayName)
    {
        _services.Settings.Update(settings => settings.PlayerName = PlayerIdentity.Normalize(displayName));
        Refresh();
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(EffectiveDisplayName));
        OnPropertyChanged(nameof(Initial));
        OnPropertyChanged(nameof(DeviceLabel));
        OnPropertyChanged(nameof(EffectiveDeviceLabel));
        OnPropertyChanged(nameof(Email));
        OnPropertyChanged(nameof(SelectedCloudRetention));
        OnPropertyChanged(nameof(UploadWhenGameCloses));
        OnPropertyChanged(nameof(ShowOnlineStatus));
    }

    // Raised after the name reached every group, so their member lists can be read again.
    public event EventHandler? DisplayNameShared;

    public async Task PushDisplayNameAsync()
    {
        if (_services.Selector.ActiveKind != CloudProviderKind.Supabase || !_services.Groups.IsSignedIn)
        {
            return;
        }

        try
        {
            foreach (var group in _services.Groups.KnownGroups)
            {
                await _services.Groups.SetDisplayNameAsync(group.Id, EffectiveDisplayName);
            }

            DisplayNameShared?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            var error = _services.ErrorHandler.Handle(exception, "Share your name with your group", AppLogKeyword.CloudSync);
            _services.Toasts.Warning("Name not updated in the group", error.UserMessage);
        }
    }
}
