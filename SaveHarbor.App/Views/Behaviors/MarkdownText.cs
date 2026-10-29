using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using SaveHarbor.App.Utilities;

namespace SaveHarbor.App.Views.Behaviors;

// Shows MarkdownLite spans in a TextBlock, with **bold** as bold runs: TextBlock.Inlines cannot be bound.
public static class MarkdownText
{
    public static readonly DependencyProperty SpansProperty = DependencyProperty.RegisterAttached(
        "Spans",
        typeof(IReadOnlyList<MarkdownSpan>),
        typeof(MarkdownText),
        new PropertyMetadata(null, OnSpansChanged));

    public static IReadOnlyList<MarkdownSpan>? GetSpans(DependencyObject element) => (IReadOnlyList<MarkdownSpan>?)element.GetValue(SpansProperty);

    public static void SetSpans(DependencyObject element, IReadOnlyList<MarkdownSpan>? value) => element.SetValue(SpansProperty, value);

    private static void OnSpansChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not TextBlock textBlock)
        {
            return;
        }

        textBlock.Inlines.Clear();
        foreach (var span in e.NewValue as IReadOnlyList<MarkdownSpan> ?? [])
        {
            textBlock.Inlines.Add(span.IsBold ? new Bold(new Run(span.Text)) : new Run(span.Text));
        }
    }
}
