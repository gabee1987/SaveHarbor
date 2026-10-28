using SaveHarbor.App.Infrastructure.Games.Enshrouded;

namespace SaveHarbor.Tests.Support;

public sealed class FixedEnshroudedLocator(EnshroudedSaveRoots roots) : IEnshroudedSaveRootLocator
{
    public EnshroudedSaveRoots Locate() => roots;
}
