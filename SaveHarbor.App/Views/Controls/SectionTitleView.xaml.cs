using System.Windows;
using System.Windows.Controls;

namespace SaveHarbor.App.Views.Controls;

public partial class SectionTitleView : UserControl
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(SectionTitleView), new PropertyMetadata(string.Empty));

    public SectionTitleView()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }
}
