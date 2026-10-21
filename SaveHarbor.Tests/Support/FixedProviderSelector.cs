using SaveHarbor.App.Infrastructure;
using SaveHarbor.App.Services;

namespace SaveHarbor.Tests.Support;

public sealed class FixedProviderSelector(string activeKind = CloudProviderKind.GoogleDrive) : ICloudProviderSelector
{
    public string ActiveKind { get; private set; } = activeKind;

    public IReadOnlyList<string> AvailableKinds { get; set; } = [CloudProviderKind.Supabase, CloudProviderKind.GoogleDrive];

    public event EventHandler? ActiveChanged;

    public Task SwitchAsync(string kind, CancellationToken cancellationToken = default)
    {
        ActiveKind = kind;
        ActiveChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }
}
