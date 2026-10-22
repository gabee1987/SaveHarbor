using System.Diagnostics;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

// Only https addresses, and http on this PC for the local development stack, so a crafted value can never start a
// program through the shell.
public sealed class SystemBrowserLauncher : IBrowserLauncher
{
    public void Open(Uri address)
    {
        if (!address.IsAbsoluteUri || !(address.Scheme == Uri.UriSchemeHttps || address.Scheme == Uri.UriSchemeHttp && address.IsLoopback))
        {
            throw new ArgumentException("Only https addresses can be opened in the browser.", nameof(address));
        }

        Process.Start(new ProcessStartInfo { FileName = address.AbsoluteUri, UseShellExecute = true })?.Dispose();
    }
}
