using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

public sealed partial class GameOptionViewModel(GameId id, string displayName) : ObservableObject
{
    public GameId Id { get; } = id;

    public string DisplayName { get; } = displayName;

    [ObservableProperty]
    private bool isActive;
}
