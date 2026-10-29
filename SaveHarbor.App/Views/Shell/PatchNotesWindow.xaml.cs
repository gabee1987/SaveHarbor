using System.Windows;

namespace SaveHarbor.App.Views.Shell;

public partial class PatchNotesWindow : Window
{
    public PatchNotesWindow(Uri skinDictionary)
    {
        InitializeComponent();

        // Merged after loading: the XAML replaces the window's resource dictionary. Skin keys are looked up dynamically.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = skinDictionary });
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
