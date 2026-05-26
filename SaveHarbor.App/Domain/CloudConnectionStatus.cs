namespace SaveHarbor.App.Domain;

public sealed record CloudConnectionStatus(
    bool IsConnected,
    string ProviderName,
    string? AccountEmail,
    string Message)
{
    public static CloudConnectionStatus NotConnected(string providerName = "None")
    {
        return new CloudConnectionStatus(false, providerName, null, "Cloud sync is not connected.");
    }
}
