using CommunityToolkit.Mvvm.ComponentModel;

namespace SaveHarbor.App.ViewModels;

public sealed partial class SettingsViewModel
{
    // The pages of the side menu, in their order.
    public const int SharingPage = 0;

    [ObservableProperty]
    private int selectedPage = SharingPage;

    // Everything about friends, groups and the cloud for the active game; the main window sets it each time Settings
    // opens and reads the cloud again when Settings closes.
    [ObservableProperty]
    private SharingViewModel? sharing;
}
