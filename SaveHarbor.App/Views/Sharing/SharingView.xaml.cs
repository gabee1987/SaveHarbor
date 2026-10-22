using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App.Views.Sharing;

public partial class SharingView : UserControl
{
    private readonly DispatcherTimer resendTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private SharingViewModel? viewModel;
    private CloudOnboardingViewModel? onboarding;

    public SharingView()
    {
        InitializeComponent();
        resendTimer.Tick += (_, _) => onboarding?.UpdateResendCountdown();
        DataContextChanged += (_, _) => Attach(DataContext as SharingViewModel);
        IsVisibleChanged += (_, _) => FocusCurrentStep();

        // Settings unloads the page when another page of its side menu is chosen, and loads it again on return.
        Loaded += (_, _) => Attach(DataContext as SharingViewModel);
        Unloaded += (_, _) => Attach(null);
    }

    private void Attach(SharingViewModel? next)
    {
        if (viewModel is not null)
        {
            viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        }

        viewModel = next;
        if (viewModel is not null)
        {
            viewModel.PropertyChanged += OnViewModelPropertyChanged;
        }

        Watch(viewModel?.Onboarding);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharingViewModel.Onboarding))
        {
            Watch(viewModel?.Onboarding);
        }
    }

    // Follows the sign-in steps: the resend countdown runs on the code step, and each step's field gets the focus.
    private void Watch(CloudOnboardingViewModel? next)
    {
        if (onboarding is not null)
        {
            onboarding.PropertyChanged -= OnOnboardingPropertyChanged;
        }

        onboarding = next;
        if (onboarding is not null)
        {
            onboarding.PropertyChanged += OnOnboardingPropertyChanged;
        }

        UpdateTimer();
        FocusCurrentStep();
    }

    private void OnOnboardingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CloudOnboardingViewModel.Step) or nameof(CloudOnboardingViewModel.GroupMode))
        {
            UpdateTimer();
            FocusCurrentStep();
        }
    }

    private void UpdateTimer()
    {
        if (onboarding?.IsCodeStep == true)
        {
            resendTimer.Start();
        }
        else
        {
            resendTimer.Stop();
        }
    }

    private void FocusCurrentStep()
    {
        if (IsVisible)
        {
            Dispatcher.BeginInvoke(new Action(() => SignInView.FocusStep(onboarding)), DispatcherPriority.Input);
        }
    }
}
