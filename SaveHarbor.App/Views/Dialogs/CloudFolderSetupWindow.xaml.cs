using System.Windows;
using System.Windows.Media;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Views.Dialogs;

public partial class CloudFolderSetupWindow : Window
{
    private readonly Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync;
    private bool lastTestSucceeded;
    private string lastTestedInput = string.Empty;

    public CloudFolderSetupWindow(
        string currentFolderId,
        Func<string, CancellationToken, Task<CloudSetupTestResult>> testAccessAsync)
    {
        InitializeComponent();
        this.testAccessAsync = testAccessAsync;
        FolderInput.Text = currentFolderId;
        FolderInput.TextChanged += (_, _) =>
        {
            lastTestSucceeded = false;
            SaveButton.IsEnabled = false;
        };
    }

    public string FolderInputValue => FolderInput.Text.Trim();

    private async void TestButton_Click(object sender, RoutedEventArgs e)
    {
        SetBusy(true, "Testing Google Drive folder access...");

        try
        {
            var input = FolderInputValue;
            var result = await testAccessAsync(input, CancellationToken.None);
            lastTestSucceeded = result.IsSuccess;
            lastTestedInput = input;
            SaveButton.IsEnabled = result.IsSuccess;
            StatusText.Text = result.Message;
            StatusText.Foreground = (Brush)(Application.Current.TryFindResource(result.IsSuccess ? "SuccessBrush" : "WarnBrush") ?? Brushes.White);
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
            StatusText.Text = "Test this folder before saving.";
            StatusText.Foreground = (Brush)(Application.Current.TryFindResource("WarnBrush") ?? Brushes.White);
            SaveButton.IsEnabled = false;
            return;
        }

        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void SetBusy(bool isBusy, string? message = null)
    {
        TestButton.IsEnabled = !isBusy;
        FolderInput.IsEnabled = !isBusy;
        if (message is not null)
        {
            StatusText.Text = message;
            StatusText.Foreground = (Brush)(Application.Current.TryFindResource("MutedBrush") ?? Brushes.White);
        }
    }
}
