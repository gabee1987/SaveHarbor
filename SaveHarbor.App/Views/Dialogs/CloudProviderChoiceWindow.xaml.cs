using System.Windows;
using System.Windows.Controls;
using SaveHarbor.App.Localization;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Views.Dialogs;

public partial class CloudProviderChoiceWindow : Window
{
    public CloudProviderChoiceWindow(Uri skinDictionary, IReadOnlyList<CloudProviderChoice> choices)
    {
        InitializeComponent();
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = skinDictionary });
        ChoiceList.ItemsSource = choices.Select(CloudProviderChoiceCard.From).ToArray();
    }

    public string? SelectedKind { get; private set; }

    private void ChooseButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedKind = (sender as Button)?.Tag as string;
        DialogResult = SelectedKind is not null;
    }
}

public sealed record CloudProviderChoiceCard(string Kind, string Title, string Description, bool IsRecommended)
{
    public string UseAutomationName => $"{UiTextCatalog.Get("CloudProviderChoice.Use")}: {Title}";

    public static CloudProviderChoiceCard From(CloudProviderChoice choice) => new(
        choice.Kind,
        UiTextCatalog.Get($"CloudProviderChoice.{choice.Kind}Title"),
        UiTextCatalog.Get($"CloudProviderChoice.{choice.Kind}Description"),
        choice.IsRecommended);
}
