using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Services;

public interface IWindroseSaveDiscoveryService
{
    Task<IReadOnlyList<WindroseWorld>> DiscoverWorldsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WindroseProfile>> DiscoverProfilesAsync(CancellationToken cancellationToken = default);
    Task<WindroseWorld?> ReadWorldAsync(string worldPath, CancellationToken cancellationToken = default);
}
