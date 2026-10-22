using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaveHarbor.App.ViewModels;

// Google sign-in on the e-mail step: the browser takes over, and the player can cancel the wait from here.
public sealed partial class CloudOnboardingViewModel
{
    private CancellationTokenSource? _browserSignIn;

    // Shown only when the cloud has Google sign-in switched on.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInWithGoogleCommand))]
    private bool canUseGoogle;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelBrowserSignInCommand))]
    private bool isWaitingForBrowser;

    private bool CanSignInWithGoogle() => !IsBusy && CanUseGoogle;

    [RelayCommand(CanExecute = nameof(CanSignInWithGoogle))]
    private Task SignInWithGoogleAsync() => RunAsync(async () =>
    {
        using var browserSignIn = new CancellationTokenSource();
        _browserSignIn = browserSignIn;
        IsWaitingForBrowser = true;
        ShowInfo("Finish signing in in your browser, then come back here.");
        try
        {
            var result = await _groups.SignInWithGoogleAsync(browserSignIn.Token);
            if (!result.IsSuccess)
            {
                ShowError(result.Message);
                return;
            }

            ShowInfo(string.Empty);
            await ContinueAfterSignInAsync();
        }
        catch (OperationCanceledException) when (browserSignIn.IsCancellationRequested)
        {
            ShowInfo(string.Empty);
        }
        finally
        {
            _browserSignIn = null;
            IsWaitingForBrowser = false;
        }
    });

    // Also called when this sign-in screen is replaced, so the app stops listening for the browser.
    [RelayCommand(CanExecute = nameof(IsWaitingForBrowser))]
    public void CancelBrowserSignIn() => _browserSignIn?.Cancel();

    private async Task LoadSignInOptionsAsync()
    {
        CanUseGoogle = await _groups.IsGoogleSignInAvailableAsync();
    }
}
