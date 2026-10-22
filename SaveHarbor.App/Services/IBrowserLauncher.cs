namespace SaveHarbor.App.Services;

// Opens a web address in the player's default browser.
public interface IBrowserLauncher
{
    void Open(Uri address);
}
