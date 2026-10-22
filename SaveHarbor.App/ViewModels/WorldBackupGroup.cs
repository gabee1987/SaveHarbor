using CommunityToolkit.Mvvm.ComponentModel;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.ViewModels;

// All backups of one world (Key is the world's name as used in backup file names), shown as a collapsible group.
public sealed partial class WorldBackupGroup(
    string key,
    string displayName,
    string folderPath,
    string summary,
    bool isOnThisPc,
    IReadOnlyList<WorldBackupItem> backups) : ObservableObject
{
    [ObservableProperty]
    private bool isExpanded;

    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    public string FolderPath { get; } = folderPath;

    public string Summary { get; } = summary;

    public bool IsOnThisPc { get; } = isOnThisPc;

    public IReadOnlyList<WorldBackupItem> Backups { get; } = backups;
}
