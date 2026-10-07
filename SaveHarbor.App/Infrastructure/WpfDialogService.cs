using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Localization;
using SaveHarbor.App.Services;
using SaveHarbor.App.ViewModels;
using SaveHarbor.App.Views.Dragonwilds;
using SaveHarbor.App.Views.Dialogs;

namespace SaveHarbor.App.Infrastructure;

public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string title, string message)
    {
        ShowDialog(title, UiTextCatalog.Get("Dialog.OperationCompleted"), message, "i", UiTextCatalog.Get("Dialog.Ok"), null, "AccentBrush");
    }

    public void ShowError(string title, string message)
    {
        ShowDialog(title, UiTextCatalog.Get("Dialog.NeedsAttention"), message, "!", UiTextCatalog.Get("Dialog.Ok"), null, "DangerBrush");
    }

    public bool Confirm(string title, string message)
    {
        return ShowDialog(
            title,
            UiTextCatalog.Get("Dialog.ConfirmAction"),
            message,
            "!",
            UiTextCatalog.Get("Dialog.Continue"),
            UiTextCatalog.Get("Dialog.Cancel"),
            "WarnBrush") == true;
    }

    public string? SelectZipFile(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select SaveHarbor backup",
            Filter = "Zip archives (*.zip)|*.zip",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    // Backups and world saves in one list first, so the user does not have to know which kind they were sent.
    public string? SelectImportFile(string initialDirectory, string saveFileFilter)
    {
        var saveFilePatterns = saveFileFilter[(saveFileFilter.IndexOf('|') + 1)..];
        var dialog = new OpenFileDialog
        {
            Title = "Import a world",
            Filter = $"World backups and saves|*.zip;{saveFilePatterns}|SaveHarbor backup (*.zip)|*.zip|{saveFileFilter}",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public void ShowWorldInspector(WorldInspectorViewModel viewModel)
    {
        new DragonwildsWorldInspectorWindow { Owner = FindOwner(), DataContext = viewModel }.ShowDialog();
    }

    public string? SelectFolder(string title, string initialDirectory)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        };

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    public CloudFolderChoice? ConfigureCloudFolder(
        string gameDisplayName,
        string currentFolderId,
        IReadOnlyList<SavedCloudFolder> savedFolders,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync,
        Func<string, Task> forgetSavedFolderAsync)
    {
        var owner = FindOwner();

        var window = new CloudFolderSetupWindow(gameDisplayName, currentFolderId, savedFolders, testAccessAsync, forgetSavedFolderAsync)
        {
            Owner = owner
        };

        return window.ShowDialog() == true ? new CloudFolderChoice(window.FolderInputValue, window.TestedFolderName) : null;
    }

    private static bool? ShowDialog(
        string title,
        string subtitle,
        string message,
        string iconText,
        string primaryText,
        string? cancelText,
        string accentResourceKey)
    {
        var owner = FindOwner();

        var accentBrush = System.Windows.Application.Current.TryFindResource(accentResourceKey) as Brush
            ?? Brushes.White;

        var window = new AppDialogWindow
        {
            Owner = owner,
            DialogTitle = title,
            Subtitle = subtitle,
            Message = message,
            IconText = iconText,
            PrimaryText = primaryText,
            CancelText = cancelText ?? string.Empty,
            CancelVisibility = string.IsNullOrWhiteSpace(cancelText) ? Visibility.Collapsed : Visibility.Visible,
            AccentBrush = accentBrush
        };

        return window.ShowDialog();
    }

    private static Window FindOwner() =>
        System.Windows.Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
        ?? System.Windows.Application.Current.MainWindow;
}
