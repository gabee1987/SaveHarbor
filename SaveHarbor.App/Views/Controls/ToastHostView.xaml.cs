using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Views.Controls;

public partial class ToastHostView : UserControl
{
    public ToastHostView()
    {
        InitializeComponent();
    }

    private void Toast_MouseEnter(object sender, MouseEventArgs e) => SetHovered(sender, true);

    private void Toast_MouseLeave(object sender, MouseEventArgs e) => SetHovered(sender, false);

    private static void SetHovered(object sender, bool isHovered)
    {
        if (sender is FrameworkElement { DataContext: ToastNotification toast })
        {
            toast.IsHovered = isHovered;
        }
    }
}
