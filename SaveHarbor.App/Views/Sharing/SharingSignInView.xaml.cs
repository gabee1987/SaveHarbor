using System.Windows.Controls;
using SaveHarbor.App.ViewModels;

namespace SaveHarbor.App.Views.Sharing;

public partial class SharingSignInView : UserControl
{
    public SharingSignInView()
    {
        InitializeComponent();
    }

    // Puts the cursor where the current step expects typing.
    public void FocusStep(CloudOnboardingViewModel? onboarding)
    {
        switch (onboarding?.Step)
        {
            case CloudOnboardingStep.Email:
                EmailInput.Focus();
                break;
            case CloudOnboardingStep.Code:
                CodeInput.FocusInput();
                break;
            case CloudOnboardingStep.Name:
                NameInput.Focus();
                NameInput.SelectAll();
                break;
            case CloudOnboardingStep.Group when onboarding.GroupMode == GroupSetupMode.Join:
                InviteInput.Focus();
                break;
            case CloudOnboardingStep.Group when onboarding.GroupMode == GroupSetupMode.Create:
                GroupNameInput.Focus();
                break;
        }
    }
}
