using System.IO;
using Microsoft.Win32;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class WpfDialogService : IDialogService
{
    public void ShowInfo(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    public void ShowError(string title, string message)
    {
        System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
    }

    public bool Confirm(string title, string message)
    {
        return System.Windows.MessageBox.Show(message, title, System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning) == System.Windows.MessageBoxResult.Yes;
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
}
