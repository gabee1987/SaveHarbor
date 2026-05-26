using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SaveHarbor.App.Services;
using SaveHarbor.App.Views.Dialogs;

namespace SaveHarbor.App.Infrastructure;

public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string title, string message)
    {
        ShowDialog(title, "Operation completed", message, "i", "OK", null, "AccentBrush");
    }

    public void ShowError(string title, string message)
    {
        ShowDialog(title, "SaveHarbor needs your attention", message, "!", "OK", null, "DangerBrush");
    }

    public bool Confirm(string title, string message)
    {
        return ShowDialog(title, "Please confirm this action", message, "!", "Continue", "Cancel", "WarnBrush") == true;
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

    private static bool? ShowDialog(
        string title,
        string subtitle,
        string message,
        string iconText,
        string primaryText,
        string? cancelText,
        string accentResourceKey)
    {
        var owner = System.Windows.Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(window => window.IsActive)
            ?? System.Windows.Application.Current.MainWindow;

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
}
