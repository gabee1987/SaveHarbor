namespace SaveHarbor.App.Domain;

public sealed record CloudConnectionStatus(
    bool IsConnected,
    string ProviderName,
    string? AccountEmail,
    string Message,
    // Something on this PC must be set up first: a provider, a sign-in or a group (see Reason).
    bool NeedsSetup = false,
    // One of CloudConnectionReason; empty when nothing specific is missing.
    string Reason = "")
{
    public static CloudConnectionStatus NotConnected(string providerName = "None")
    {
        return new CloudConnectionStatus(false, providerName, null, "Cloud sync is not connected.");
    }
}

public static class CloudConnectionReason
{
    public const string NotConfigured = "NotConfigured";
    public const string SignIn = "SignIn";
    public const string Group = "Group";
    public const string UpdateRequired = "UpdateRequired";
    public const string Unreachable = "Unreachable";
}
