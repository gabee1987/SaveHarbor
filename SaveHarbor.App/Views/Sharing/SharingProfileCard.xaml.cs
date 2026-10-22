using System.Windows;
using System.Windows.Controls;

namespace SaveHarbor.App.Views.Sharing;

public partial class SharingProfileCard : UserControl
{
    // The e-mail and "Sign out", for SaveHarbor Cloud only.
    public static readonly DependencyProperty ShowAccountProperty = DependencyProperty.Register(
        nameof(ShowAccount), typeof(bool), typeof(SharingProfileCard), new PropertyMetadata(false));

    public SharingProfileCard()
    {
        InitializeComponent();
    }

    public bool ShowAccount
    {
        get => (bool)GetValue(ShowAccountProperty);
        set => SetValue(ShowAccountProperty, value);
    }
}
