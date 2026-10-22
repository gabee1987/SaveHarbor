using System.IO;
using System.Security;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Infrastructure;

namespace SaveHarbor.App.ViewModels;

// saveharbor://join links: the installer registers them; this registers them again for this copy of the app, for
// example one started without the installer.
public sealed partial class SettingsViewModel
{
    public bool AreInviteLinksRegistered =>
        Environment.ProcessPath is { } exe
        && ProtocolRegistration.RegisteredCommand()?.Contains(exe, StringComparison.OrdinalIgnoreCase) == true;

    public string InviteLinksStatus => AreInviteLinksRegistered
        ? "Invite links from friends open this copy of SaveHarbor."
        : "Invite links from friends do not open this copy of SaveHarbor yet.";

    [RelayCommand]
    private void RegisterInviteLinks()
    {
        try
        {
            ProtocolRegistration.Register();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            _dialogService.ShowError("Invite links not registered", $"Windows did not allow SaveHarbor to register its links: {ex.Message}");
        }

        OnPropertyChanged(nameof(AreInviteLinksRegistered));
        OnPropertyChanged(nameof(InviteLinksStatus));
    }
}
