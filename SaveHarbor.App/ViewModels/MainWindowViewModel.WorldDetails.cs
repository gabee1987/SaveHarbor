using System.Collections.ObjectModel;
using System.IO;
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

    // Backup file names are "<timestamp>_<safe world name>_<reason>.zip" (ZipBackupService).
    private void RefreshSelectedWorldBackups()
    {
        SelectedWorldBackups.Clear();
        if (SelectedWorld is null)
        {
            return;
        }

        var marker = $"_{FileNameSanitizer.MakeSafeFileName(SelectedWorld.WorldName)}_";
        foreach (var backup in gameBackups
                     .Where(backup => backup.FileName.Contains(marker, StringComparison.OrdinalIgnoreCase))
                     .Take(RecentWorldBackupLimit))
        {
            var stem = Path.GetFileNameWithoutExtension(backup.FileName);
            var reason = stem[(stem.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length)..];
            SelectedWorldBackups.Add(new WorldBackupItem(backup.FilePath, backup.CreatedAt, DescribeBackupReason(reason), backup.SizeBytes));
        }
    }

    private static string DescribeBackupReason(string reason) => reason switch
    {
        "manual" => "Manual backup",
        "pre-restore" => "Before restore",
        "cloud-upload" => "Shared to cloud",
        _ => reason
    };
}
