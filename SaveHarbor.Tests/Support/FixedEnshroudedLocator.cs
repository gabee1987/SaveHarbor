using SaveHarbor.App.Infrastructure.Games.Enshrouded;

namespace SaveHarbor.Tests.Support;

public sealed class FixedEnshroudedLocator(EnshroudedSaveRoot? root) : IEnshroudedSaveRootLocator
{
    public EnshroudedSaveRoot? Locate() => root;
}
