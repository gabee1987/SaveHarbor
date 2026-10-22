using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

// Backs the "All world info" window. Read-only, except for "Use this copy", which runs the normal import flow
// (preview, comparison, safety backups) on the game's own backup copy of the world.
public sealed partial class WorldInspectorViewModel : ObservableObject
{
    private readonly GameWorld _world;
    private readonly IGameSaveAdapter _adapter;
    private readonly string _backupRoot;
    private readonly Func<IReadOnlyList<InspectionItem>> _describeSafety;
    private readonly Func<string, Task> _importSaveFile;
    private readonly Func<GameWorld, Task> _moveLeftoversAside;

    public WorldInspectorViewModel(
        GameWorld world,
        string privacyNote,
        IGameSaveAdapter adapter,
        string backupRoot,
        Func<IReadOnlyList<InspectionItem>> describeSafety,
        Func<string, Task> importSaveFile,
        Func<GameWorld, Task> moveLeftoversAside)
    {
        _world = world;
        PrivacyNote = privacyNote;
        _adapter = adapter;
        _backupRoot = backupRoot;
        _describeSafety = describeSafety;
        _importSaveFile = importSaveFile;
        _moveLeftoversAside = moveLeftoversAside;
    }

    public string WorldName => _world.WorldName;

    // What this game's inspector deliberately leaves out; worded per game.
    public string PrivacyNote { get; }

    public ObservableCollection<InspectionSection> Sections { get; } = [];

    // Problems the game's save checks found; the card above the sections offers the fixes.
    public ObservableCollection<SaveHealthNotice> HealthProblems { get; } = [];

    public bool HasHealthProblems => HealthProblems.Count > 0;

    public bool HasLeftovers => HealthProblems.Any(notice => notice.Issue == SaveHealthIssue.Leftovers);

    public bool IsWorldReplaced => HealthProblems.Any(notice => notice.Issue == SaveHealthIssue.WorldReplaced);

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? gameBackupCopyPath;

    public async Task LoadAsync()
    {
        IsLoading = true;
        Sections.Clear();
        HealthProblems.Clear();
        try
        {
            var health = _adapter.CheckHealth(_world);
            if (health is not null)
            {
                foreach (var notice in health)
                {
                    HealthProblems.Add(notice);
                }

                Sections.Add(new InspectionSection("Save health", health.Count == 0
                    ? [new InspectionItem("Status", "No problems found")]
                    : health.Select(notice => new InspectionItem(notice.Title, notice.Detail)).ToArray()));
            }

            foreach (var section in await _adapter.InspectWorldAsync(_world))
            {
                Sections.Add(section);
            }
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            Sections.Add(new InspectionSection("Save file", [new InspectionItem("Could not read", ex.Message)]));
        }

        Sections.Add(new InspectionSection("SaveHarbor backups", _describeSafety()));
        GameBackupCopyPath = _adapter.GetGameBackupCopyPath(_world);
        OnPropertyChanged(nameof(HasHealthProblems));
        OnPropertyChanged(nameof(HasLeftovers));
        OnPropertyChanged(nameof(IsWorldReplaced));
        IsLoading = false;
    }

    [RelayCommand]
    private void ShowWorldFile() => ExplorerLauncher.ShowFile(_world.SavePath);

    [RelayCommand]
    private void OpenSaveFolder() => ExplorerLauncher.OpenExistingFolder(_adapter.SaveRootPath);

    [RelayCommand]
    private void OpenBackupFolder() => ExplorerLauncher.OpenFolder(_backupRoot);

    [RelayCommand]
    private void ShowGameBackupCopy()
    {
        if (GameBackupCopyPath is not null)
        {
            ExplorerLauncher.ShowFile(GameBackupCopyPath);
        }
    }

    [RelayCommand]
    private async Task MoveLeftoversAsideAsync()
    {
        await _moveLeftoversAside(_world);
        await LoadAsync();
    }

    // The user checked the world in the game and wants it as it is: its current files become the new baseline.
    [RelayCommand]
    private async Task KeepWorldAsItIsAsync()
    {
        _adapter.RememberWorldState(_world.SavePath);
        await LoadAsync();
    }

    [RelayCommand]
    private async Task UseGameBackupCopyAsync()
    {
        if (GameBackupCopyPath is null)
        {
            return;
        }

        await _importSaveFile(GameBackupCopyPath);
        await LoadAsync();
    }
}
