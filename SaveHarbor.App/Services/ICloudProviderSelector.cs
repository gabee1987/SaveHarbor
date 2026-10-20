namespace SaveHarbor.App.Services;

public interface ICloudProviderSelector
{
    // "GoogleDrive", "Supabase", or "LocalTest" (developer override); empty while no provider is chosen on this PC.
    string ActiveKind { get; }

    // Makes the given provider ("GoogleDrive" or "Supabase") the active one and remembers the choice.
    Task SwitchAsync(string kind, CancellationToken cancellationToken = default);

    event EventHandler? ActiveChanged;
}
