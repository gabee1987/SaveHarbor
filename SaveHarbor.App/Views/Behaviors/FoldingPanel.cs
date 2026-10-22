using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SaveHarbor.App.Views.Behaviors;

// A main window panel that folds into a rail: on a Grid whose first child is the rail and second the panel. Folding
// eases the width between the two and cross-fades them; with Windows animations off, or while the window loads, it
// switches at once.
public static class FoldingPanel
{
    private static readonly Duration WidthDuration = TimeSpan.FromMilliseconds(260);
    private static readonly Duration FadeDuration = TimeSpan.FromMilliseconds(170);

    // Nullable, so the first value from the binding always applies, also when it is false.
    public static readonly DependencyProperty IsExpandedProperty = DependencyProperty.RegisterAttached(
        "IsExpanded", typeof(bool?), typeof(FoldingPanel), new PropertyMetadata(null, OnIsExpandedChanged));

    public static readonly DependencyProperty ExpandedWidthProperty = DependencyProperty.RegisterAttached(
        "ExpandedWidth", typeof(double), typeof(FoldingPanel), new PropertyMetadata(250d));

    public static readonly DependencyProperty CollapsedWidthProperty = DependencyProperty.RegisterAttached(
        "CollapsedWidth", typeof(double), typeof(FoldingPanel), new PropertyMetadata(60d));

    public static bool? GetIsExpanded(DependencyObject element) => (bool?)element.GetValue(IsExpandedProperty);

    public static void SetIsExpanded(DependencyObject element, bool? value) => element.SetValue(IsExpandedProperty, value);

    public static double GetExpandedWidth(DependencyObject element) => (double)element.GetValue(ExpandedWidthProperty);

    public static void SetExpandedWidth(DependencyObject element, double value) => element.SetValue(ExpandedWidthProperty, value);

    public static double GetCollapsedWidth(DependencyObject element) => (double)element.GetValue(CollapsedWidthProperty);

    public static void SetCollapsedWidth(DependencyObject element, double value) => element.SetValue(CollapsedWidthProperty, value);

    private static void OnIsExpandedChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not Grid { Children.Count: >= 2 } grid || e.NewValue is not bool expanded)
        {
            return;
        }

        var animate = grid.IsLoaded && e.OldValue is bool && SystemParameters.ClientAreaAnimation;
        var rail = grid.Children[0];
        var panel = grid.Children[1];
        var shown = expanded ? panel : rail;
        var hidden = expanded ? rail : panel;
        var width = expanded ? GetExpandedWidth(grid) : GetCollapsedWidth(grid);
        grid.ClipToBounds = true;
        shown.Visibility = Visibility.Visible;

        if (!animate)
        {
            grid.BeginAnimation(FrameworkElement.WidthProperty, null);
            shown.BeginAnimation(UIElement.OpacityProperty, null);
            hidden.BeginAnimation(UIElement.OpacityProperty, null);
            grid.Width = width;
            shown.Opacity = 1;
            hidden.Visibility = Visibility.Collapsed;
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        grid.BeginAnimation(FrameworkElement.WidthProperty, new DoubleAnimation(grid.ActualWidth, width, WidthDuration) { EasingFunction = ease });

        // The panel appears once the width has opened a little, and the one leaving fades out first.
        shown.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, FadeDuration)
        {
            BeginTime = expanded ? TimeSpan.FromMilliseconds(90) : TimeSpan.Zero
        });
        var fadeOut = new DoubleAnimation(hidden.Opacity, 0, FadeDuration);
        fadeOut.Completed += (_, _) =>
        {
            if (GetIsExpanded(grid) == expanded)
            {
                hidden.Visibility = Visibility.Collapsed;
            }
        };
        hidden.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
}
