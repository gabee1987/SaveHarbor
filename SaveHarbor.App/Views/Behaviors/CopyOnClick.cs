using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SaveHarbor.App.Views.Behaviors;

public static class CopyOnClick
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text",
        typeof(string),
        typeof(CopyOnClick),
        new PropertyMetadata(null, OnTextChanged));

    public static string? GetText(DependencyObject element)
    {
        return (string?)element.GetValue(TextProperty);
    }

    public static void SetText(DependencyObject element, string? value)
    {
        element.SetValue(TextProperty, value);
    }

    private static void OnTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        element.MouseLeftButtonUp -= CopyText;

        if (!string.IsNullOrWhiteSpace(e.NewValue as string))
        {
            element.MouseLeftButtonUp += CopyText;
        }
        else
        {
            element.MouseLeftButtonUp -= CopyText;
        }
    }

    private static void CopyText(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        var text = GetText(element);
        if (string.IsNullOrWhiteSpace(text) && element is TextBlock textBlock)
        {
            text = textBlock.Text;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Clipboard.SetText(text);
        e.Handled = true;
    }
}
