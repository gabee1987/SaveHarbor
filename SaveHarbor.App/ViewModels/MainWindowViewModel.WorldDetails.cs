using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private const int WorldBackupListLimit = 100;

    private IReadOnlyList<BackupInfo> gameBackups = [];

    public ObservableCollection<WorldFact> SelectedWorldDetails { get; } = [];

    public ObservableCollection<WorldFact> SelectedWorldRules { get; } = [];

    public ObservableCollection<WorldBackupItem> SelectedWorldBackups { get; } = [];

    // Backups of every other world, one group per world, including worlds no longer on this PC.
    public ObservableCollection<WorldBackupGroup> OtherWorldBackupGroups { get; } = [];

    public string SelectedWorldBackupSummary => SelectedWorldBackups.Count == 0
        ? "No backups yet"
        : $"{SelectedWorldBackups.Count} backup{(SelectedWorldBackups.Count == 1 ? string.Empty : "s")} · {DisplayFormatter.FormatBytes(SelectedWorldBackups.Sum(backup => backup.SizeBytes))}";

    public string SelectedWorldBackupFolder => SelectedWorld is null
        ? BackupRoot
        : _backupService.GetWorldBackupFolder(_activeGame.Current.Id, SelectedWorld.WorldName);

    private async Task RefreshWorldFactsAsync()
    {
        var world = SelectedWorld;
        SelectedWorldDetails.Clear();
        SelectedWorldRules.Clear();
        if (world is null)
        {
            return;
        }

        try
        {
            var facts = await _activeGame.Current.SaveAdapter.ReadWorldFactsAsync(world);
            if (!ReferenceEquals(world, SelectedWorld))
            {
                return;
            }

            foreach (var fact in facts)
            {
                (fact.Group == WorldFactGroup.Rules ? SelectedWorldRules : SelectedWorldDetails).Add(fact);
            }
        }
        catch (Exception ex)
        {
            _errorHandler.Handle(ex, "Read world details", AppLogKeyword.Discovery);
        }
    }

    private IEnumerable<BackupInfo> BackupsOf(GameWorld world)
    {
        var safeName = FileNameSanitizer.MakeSafeFileName(world.WorldName);
        return gameBackups.Where(backup =>
            BackupFileName.TryParse(backup.FileName, out var name, out _) && string.Equals(name, safeName, StringComparison.OrdinalIgnoreCase));
    }

    private void RefreshSelectedWorldBackups()
    {
        OnPropertyChanged(nameof(SelectedWorldBackupFolder));
        SelectedWorldBackups.Clear();
        if (SelectedWorld is not null)
        {
            foreach (var backup in BackupsOf(SelectedWorld).Take(WorldBackupListLimit))
            {
                SelectedWorldBackups.Add(ToBackupItem(backup));
            }
        }

        OnPropertyChanged(nameof(SelectedWorldBackupSummary));
        RefreshOtherWorldBackupGroups();
    }

    private void RefreshOtherWorldBackupGroups()
    {
        var expanded = OtherWorldBackupGroups.Where(group => group.IsExpanded).Select(group => group.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var selectedKey = SelectedWorld is null ? null : FileNameSanitizer.MakeSafeFileName(SelectedWorld.WorldName);
        var localNames = Worlds.ToDictionary(world => FileNameSanitizer.MakeSafeFileName(world.WorldName), world => world.WorldName, StringComparer.OrdinalIgnoreCase);

        var groups = gameBackups
            .Select(backup => (Backup: backup, Parsed: BackupFileName.TryParse(backup.FileName, out var name, out _), Name: name))
            .Where(item => item.Parsed && !string.Equals(item.Name, selectedKey, StringComparison.OrdinalIgnoreCase))
            .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var backups = group.Select(item => item.Backup).OrderByDescending(backup => backup.CreatedAt).ToArray();
                var isLocal = localNames.TryGetValue(group.Key, out var worldName);
                return new WorldBackupGroup(
                    group.Key,
                    worldName ?? group.Key,
                    _backupService.GetWorldBackupFolder(_activeGame.Current.Id, group.Key),
                    $"{backups.Length} backup{(backups.Length == 1 ? string.Empty : "s")} · {DisplayFormatter.FormatBytes(backups.Sum(backup => backup.SizeBytes))} · newest {DisplayFormatter.FormatAge(backups[0].CreatedAt)}",
                    isLocal,
                    backups.Take(WorldBackupListLimit).Select(ToBackupItem).ToArray())
                {
                    IsExpanded = expanded.Contains(group.Key)
                };
            })
            .OrderBy(group => group.IsOnThisPc)
            .ThenBy(group => group.DisplayName, StringComparer.OrdinalIgnoreCase);

        OtherWorldBackupGroups.Clear();
        foreach (var group in groups)
        {
            OtherWorldBackupGroups.Add(group);
        }
    }

    private static WorldBackupItem ToBackupItem(BackupInfo backup)
    {
        BackupFileName.TryParse(backup.FileName, out _, out var reason);
        return new WorldBackupItem(backup.FilePath, backup.CreatedAt, DescribeBackupReason(reason), backup.SizeBytes);
    }

    [RelayCommand(CanExecute = nameof(HasSelectedWorld))]
    private void OpenWorldInspector()
    {
        if (SelectedWorld is null)
        {
            return;
        }

        var world = SelectedWorld;
        var inspector = new WorldInspectorViewModel(
            world,
            _activeGame.Current.SaveAdapter,
            _backupService.GetBackupRoot(_activeGame.Current.Id),
            () => DescribeBackupsOf(world),
            ImportSaveFileAsync);
        _ = inspector.LoadAsync();
        _dialogService.ShowWorldInspector(inspector, _activeGame.Current.SkinDictionary);
    }

    private IReadOnlyList<InspectionItem> DescribeBackupsOf(GameWorld world)
    {
        var backups = BackupsOf(world).ToArray();
        List<InspectionItem> items = [new("Backups of this world", backups.Length.ToString(CultureInfo.InvariantCulture))];
        if (backups.Length > 0)
        {
            BackupFileName.TryParse(backups[0].FileName, out _, out var reason);
            items.Add(new InspectionItem("Newest backup", $"{backups[0].CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm} ({DescribeBackupReason(reason)})"));
            items.Add(new InspectionItem("Oldest backup", $"{backups[^1].CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}"));
            items.Add(new InspectionItem("Space used", DisplayFormatter.FormatBytes(backups.Sum(backup => backup.SizeBytes))));
        }

        items.Add(new InspectionItem("Backup folder", _backupService.GetBackupRoot(_activeGame.Current.Id)));
        return items;
    }

    private static string DescribeBackupReason(string reason) => reason switch
    {
        BackupReasons.Manual => "Manual backup",
        BackupReasons.PreRestore => "Before restore",
        BackupReasons.PreImport => "Before import",
        BackupReasons.Imported => "Imported file",
        BackupReasons.CloudUpload => "Shared to cloud",
        BackupReasons.PreRemove => "Before removal from PC",
        BackupReasons.CloudRemoved => "Removed from cloud",
        _ => reason
    };
}
