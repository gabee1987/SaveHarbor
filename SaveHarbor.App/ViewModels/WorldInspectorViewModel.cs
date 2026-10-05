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

    public WorldInspectorViewModel(
        GameWorld world,
        IGameSaveAdapter adapter,
        string backupRoot,
        Func<IReadOnlyList<InspectionItem>> describeSafety,
        Func<string, Task> importSaveFile)
    {
        _world = world;
        _adapter = adapter;
        _backupRoot = backupRoot;
        _describeSafety = describeSafety;
        _importSaveFile = importSaveFile;
    }

    public string WorldName => _world.WorldName;

    public ObservableCollection<InspectionSection> Sections { get; } = [];

    [ObservableProperty]
    private bool isLoading;

    [ObservableProperty]
    private string? gameBackupCopyPath;

    public async Task LoadAsync()
    {
        IsLoading = true;
        Sections.Clear();
        try
        {
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
