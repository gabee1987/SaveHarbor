using CommunityToolkit.Mvvm.Input;

namespace SaveHarbor.App.ViewModels;

// The world list and the world details can fold into a narrow rail, so the Sharing panel gets the room. The choice
// is remembered.
public partial class MainWindowViewModel
{
    public bool IsWorldListExpanded => !_settingsStore.Current.WorldListCollapsed;

    public bool IsWorldDetailsExpanded => !_settingsStore.Current.WorldDetailsCollapsed;

    [RelayCommand]
    private void ToggleWorldList()
    {
        _settingsStore.Update(settings => settings.WorldListCollapsed = !settings.WorldListCollapsed);
        OnPropertyChanged(nameof(IsWorldListExpanded));
    }

    [RelayCommand]
    private void ToggleWorldDetails()
    {
        _settingsStore.Update(settings => settings.WorldDetailsCollapsed = !settings.WorldDetailsCollapsed);
        OnPropertyChanged(nameof(IsWorldDetailsExpanded));
    }
}
