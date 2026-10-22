using System.Windows;
using System.Windows.Controls;

namespace SaveHarbor.App.Views.Controls;

public partial class IndeterminateProgressLineView : UserControl
{
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
        nameof(IsActive),
        typeof(bool),
        typeof(IndeterminateProgressLineView),
        new PropertyMetadata(false));

    public IndeterminateProgressLineView()
    {
        InitializeComponent();
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }
}
