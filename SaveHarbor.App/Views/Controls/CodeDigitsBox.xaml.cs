using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SaveHarbor.App.Views.Controls;

public sealed record CodeDigitCell(string Digit, bool IsFilled, bool IsActive);

// Code binds two-way to the view model; the sixth digit runs CompleteCommand, so a pasted code needs no extra click.
public partial class CodeDigitsBox : UserControl
{
    public const int Length = 6;

    public static readonly DependencyProperty CodeProperty = DependencyProperty.Register(
        nameof(Code), typeof(string), typeof(CodeDigitsBox),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCodeChanged));

    public static readonly DependencyProperty CompleteCommandProperty = DependencyProperty.Register(
        nameof(CompleteCommand), typeof(ICommand), typeof(CodeDigitsBox));

    public CodeDigitsBox()
    {
        InitializeComponent();
        DataObject.AddPastingHandler(Input, OnPasting);
        Render();
    }

    public string Code
    {
        get => (string)GetValue(CodeProperty);
        set => SetValue(CodeProperty, value);
    }

    public ICommand? CompleteCommand
    {
        get => (ICommand?)GetValue(CompleteCommandProperty);
        set => SetValue(CompleteCommandProperty, value);
    }

    public void FocusInput() => Input.Focus();

    // Keeps only the digits of a code, at most six: "123 456" and "123-456" both become "123456".
    public static string DigitsOf(string? text) => new((text ?? string.Empty).Where(char.IsAsciiDigit).Take(Length).ToArray());

    private static void OnCodeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        var box = (CodeDigitsBox)sender;
        var digits = DigitsOf(e.NewValue as string);
        if (box.Input.Text != digits)
        {
            box.Input.Text = digits;
            box.Input.CaretIndex = digits.Length;
        }

        box.Render();
    }

    private void Input_PreviewTextInput(object sender, TextCompositionEventArgs e) => e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text)
        {
            Input.Text = DigitsOf(text);
            Input.CaretIndex = Input.Text.Length;
        }
    }

    private void Input_TextChanged(object sender, TextChangedEventArgs e)
    {
        var digits = DigitsOf(Input.Text);
        if (Code != digits)
        {
            Code = digits;
        }

        Render();
        if (digits.Length == Length && CompleteCommand?.CanExecute(null) == true)
        {
            CompleteCommand.Execute(null);
        }
    }

    private void Input_FocusChanged(object sender, KeyboardFocusChangedEventArgs e) => Render();

    private void Render()
    {
        var digits = DigitsOf(Input.Text);
        var focused = Input.IsKeyboardFocused;
        Cells.ItemsSource = Enumerable.Range(0, Length)
            .Select(index => new CodeDigitCell(
                index < digits.Length ? digits[index].ToString() : string.Empty,
                index < digits.Length,
                focused && index == Math.Min(digits.Length, Length - 1)))
            .ToArray();
    }
}
