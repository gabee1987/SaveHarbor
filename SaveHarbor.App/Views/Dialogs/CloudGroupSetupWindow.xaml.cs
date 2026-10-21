using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using SaveHarbor.App.Localization;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App.Views.Dialogs;

public partial class CloudGroupSetupWindow : Window
{
    private readonly CloudGroupSetupViewModel viewModel;
    private readonly DispatcherTimer resendTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    public CloudGroupSetupWindow(Uri skinDictionary, string gameDisplayName, CloudGroupSetupViewModel viewModel)
    {
        InitializeComponent();

        // Shared by both games; link buttons use the active game's own style.
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = skinDictionary });
        var title = string.Format(UiTextCatalog.Get("CloudGroup.TitleFormat"), gameDisplayName);
        Title = title;
        TitleText.Text = title;

        this.viewModel = viewModel;
        DataContext = viewModel;
        viewModel.CloseRequested += OnCloseRequested;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        DataObject.AddPastingHandler(CodeInput, OnCodePasting);
        resendTimer.Tick += (_, _) => viewModel.UpdateResendCountdown();

        Loaded += async (_, _) =>
        {
            FocusStep();
            await viewModel.InitializeAsync();
        };
        Closed += (_, _) =>
        {
            resendTimer.Stop();
            viewModel.CloseRequested -= OnCloseRequested;
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        };
    }

    private void OnCloseRequested(object? sender, EventArgs e) => DialogResult = true;

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CloudGroupSetupViewModel.Step))
        {
            return;
        }

        if (viewModel.IsCodeStep)
        {
            resendTimer.Start();
        }
        else
        {
            resendTimer.Stop();
        }

        Dispatcher.BeginInvoke(new Action(FocusStep), DispatcherPriority.Input);
    }

    private void FocusStep()
    {
        TextBox? target = viewModel.Step switch
        {
            CloudGroupSetupStep.Email => EmailInput,
            CloudGroupSetupStep.Code => CodeInput,
            CloudGroupSetupStep.Group => InviteInput,
            _ => null
        };
        target?.Focus();
    }

    // Digits only; a pasted code such as "123 456" keeps its digits.
    private void CodeInput_PreviewTextInput(object sender, TextCompositionEventArgs e) =>
        e.Handled = !e.Text.All(char.IsAsciiDigit);

    private void OnCodePasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        if (e.DataObject.GetData(DataFormats.UnicodeText) is string text)
        {
            CodeInput.Text = new string(text.Where(char.IsAsciiDigit).Take(6).ToArray());
            CodeInput.CaretIndex = CodeInput.Text.Length;
        }
    }

    // The sixth digit signs in, so a pasted code needs no extra click.
    private void CodeInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CodeInput.Text.Length == 6 && viewModel.VerifyCommand.CanExecute(null))
        {
            viewModel.VerifyCommand.Execute(null);
        }
    }
}
