using System.Windows;
using System.Windows.Media;

namespace SaveHarbor.App.Views.Dialogs;

public partial class AppDialogWindow : Window
{
    public AppDialogWindow()
    {
        InitializeComponent();
    }

    public string DialogTitle { get; init; } = string.Empty;

    public string Subtitle { get; init; } = string.Empty;

    public string Message { get; init; } = string.Empty;

    public string IconText { get; init; } = "i";

    public string PrimaryText { get; init; } = "OK";

    public string CancelText { get; init; } = "Cancel";

    public Visibility CancelVisibility { get; init; } = Visibility.Collapsed;

    public Brush AccentBrush { get; init; } = Brushes.White;

    private void PrimaryButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
