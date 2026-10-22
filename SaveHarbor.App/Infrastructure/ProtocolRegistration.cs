using Microsoft.Win32;

namespace SaveHarbor.App.Infrastructure;

// Registers saveharbor:// links for the current Windows user only (HKCU, no administrator rights), so an invite link
// opens SaveHarbor. The installer registers it after install and update and removes it before uninstall; Settings can
// register it again, for example for a copy started without the installer.
public static class ProtocolRegistration
{
    private const string KeyPath = @"Software\Classes\" + InviteLink.Scheme;

    public static void Register(string? executablePath = null)
    {
        var exe = executablePath ?? Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
        {
            return;
        }

        using var key = Registry.CurrentUser.CreateSubKey(KeyPath);
        key.SetValue(string.Empty, "URL:SaveHarbor invite link");
        key.SetValue("URL Protocol", string.Empty);
        using var command = key.CreateSubKey(@"shell\open\command");
        command.SetValue(string.Empty, $"\"{exe}\" \"%1\"");
    }

    public static void Unregister() => Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);

    // The registered command, or null when links are not registered.
    public static string? RegisteredCommand()
    {
        using var command = Registry.CurrentUser.OpenSubKey(KeyPath + @"\shell\open\command");
        return command?.GetValue(string.Empty) as string;
    }
}
