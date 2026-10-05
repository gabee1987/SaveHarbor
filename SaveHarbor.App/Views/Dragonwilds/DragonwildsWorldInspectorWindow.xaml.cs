using System.Windows;

namespace SaveHarbor.App.Views.Dragonwilds;

public partial class DragonwildsWorldInspectorWindow : Window
{
    public DragonwildsWorldInspectorWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
