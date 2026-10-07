using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Localization;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Views.Dialogs;

public partial class CloudFolderSetupWindow : Window
{
    private readonly Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync;
    private readonly Func<string, Task> forgetSavedFolderAsync;
    private readonly ObservableCollection<SavedFolderOption> savedFolders;
    private readonly string currentFolderId;
    private bool lastTestSucceeded;
    private bool isSelectingSilently;
    private string lastTestedInput = string.Empty;

    public CloudFolderSetupWindow(
        string gameDisplayName,
        string currentFolderId,
        IReadOnlyList<SavedCloudFolder> savedFolders,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync,
        Func<string, Task> forgetSavedFolderAsync)
    {
        InitializeComponent();
        var title = string.Format(UiTextCatalog.Get("CloudSetup.TitleFormat"), gameDisplayName);
        Title = title;
        TitleText.Text = title;
        this.testAccessAsync = testAccessAsync;
        this.forgetSavedFolderAsync = forgetSavedFolderAsync;
        this.currentFolderId = currentFolderId;
        this.savedFolders = new ObservableCollection<SavedFolderOption>(savedFolders.Select(folder => SavedFolderOption.From(folder, currentFolderId)));
        SavedFolderPicker.ItemsSource = this.savedFolders;
        UpdateSavedFoldersVisibility();

        FolderInput.Text = currentFolderId;
        FolderInput.TextChanged += (_, _) =>
        {
            lastTestSucceeded = false;
            SaveButton.IsEnabled = false;
        };

        // The current folder is shown as chosen without testing it, so opening the dialog never asks to sign in.
        isSelectingSilently = true;
        SavedFolderPicker.SelectedItem = this.savedFolders.FirstOrDefault(option => option.FolderId == currentFolderId);
        isSelectingSilently = false;
    }

    public string FolderInputValue => FolderInput.Text.Trim();

    public string? TestedFolderName { get; private set; }

    private async void SavedFolderPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var option = SavedFolderPicker.SelectedItem as SavedFolderOption;
        ForgetButton.IsEnabled = option is not null && option.FolderId != currentFolderId;
        if (option is null || isSelectingSilently)
        {
            return;
        }

        FolderInput.Text = option.FolderId;
        await TestAsync();
    }

    private async void ForgetButton_Click(object sender, RoutedEventArgs e)
    {
        if (SavedFolderPicker.SelectedItem is not SavedFolderOption option || option.FolderId == currentFolderId)
        {
            return;
        }

        await forgetSavedFolderAsync(option.FolderId);
        isSelectingSilently = true;
        savedFolders.Remove(option);
        SavedFolderPicker.SelectedItem = null;
        isSelectingSilently = false;
        ForgetButton.IsEnabled = false;
        UpdateSavedFoldersVisibility();
        ShowStatus(string.Format(UiTextCatalog.Get("CloudSetup.Forgotten"), option.Label), "MutedBrush");
    }

    private async void TestButton_Click(object sender, RoutedEventArgs e) => await TestAsync();

    private async Task TestAsync()
    {
        SetBusy(true, "Testing Google Drive folder access...");

        try
        {
            var input = FolderInputValue;
            var result = await testAccessAsync(input, CancellationToken.None);
            lastTestSucceeded = result.IsSuccess;
            lastTestedInput = input;
            TestedFolderName = result.IsSuccess ? result.FolderName : null;
            SaveButton.IsEnabled = result.IsSuccess;
            ShowStatus(result.Message, result.IsSuccess ? "SuccessBrush" : "WarnBrush");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (!lastTestSucceeded || !string.Equals(lastTestedInput, FolderInputValue, StringComparison.Ordinal))
        {
            ShowStatus("Test this folder before saving.", "WarnBrush");
            SaveButton.IsEnabled = false;
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void UpdateSavedFoldersVisibility()
    {
        SavedFoldersPanel.Visibility = savedFolders.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)(Application.Current.TryFindResource(brushKey) ?? Brushes.White);
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        TestButton.IsEnabled = !isBusy;
        FolderInput.IsEnabled = !isBusy;
        SavedFolderPicker.IsEnabled = !isBusy;
        SetupProgressLine.IsActive = isBusy;
        if (message is not null)
        {
            ShowStatus(message, "MutedBrush");
        }
    }

    public sealed record SavedFolderOption(string FolderId, string Label)
    {
        public static SavedFolderOption From(SavedCloudFolder folder, string currentFolderId)
        {
            var name = string.IsNullOrWhiteSpace(folder.Name) ? UiTextCatalog.Get("CloudSetup.UnnamedFolder") : folder.Name;
            return new SavedFolderOption(
                folder.FolderId,
                folder.FolderId == currentFolderId ? string.Format(UiTextCatalog.Get("CloudSetup.CurrentFolderFormat"), name) : name);
        }
    }
}
