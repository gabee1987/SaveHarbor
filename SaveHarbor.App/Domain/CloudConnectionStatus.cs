namespace SaveHarbor.App.Domain;

public sealed record CloudConnectionStatus(
    bool IsConnected,
    string ProviderName,
    string? AccountEmail,
    string Message,
    // No cloud provider has been chosen on this PC yet.
    bool NeedsSetup = false)
{
    public static CloudConnectionStatus NotConnected(string providerName = "None")
    {
        return new CloudConnectionStatus(false, providerName, null, "Cloud sync is not connected.");
    }
}
