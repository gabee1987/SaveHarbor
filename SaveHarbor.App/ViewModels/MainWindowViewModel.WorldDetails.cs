using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.ViewModels;

public partial class MainWindowViewModel
{
    private const int RecentWorldBackupLimit = 5;

    private IReadOnlyList<BackupInfo> gameBackups = [];

    public ObservableCollection<WorldFact> SelectedWorldDetails { get; } = [];

    public ObservableCollection<WorldFact> SelectedWorldRules { get; } = [];

    public ObservableCollection<WorldBackupItem> SelectedWorldBackups { get; } = [];

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
        SelectedWorldBackups.Clear();
        if (SelectedWorld is null)
        {
            return;
        }

        foreach (var backup in BackupsOf(SelectedWorld).Take(RecentWorldBackupLimit))
        {
            BackupFileName.TryParse(backup.FileName, out _, out var reason);
            SelectedWorldBackups.Add(new WorldBackupItem(backup.FilePath, backup.CreatedAt, DescribeBackupReason(reason), backup.SizeBytes));
        }
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
        _dialogService.ShowWorldInspector(inspector);
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
        _ => reason
    };
}
