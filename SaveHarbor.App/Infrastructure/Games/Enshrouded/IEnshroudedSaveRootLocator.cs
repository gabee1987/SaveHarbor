namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// SteamUserId is the numeric userdata folder name; it is only used to tell several Steam accounts apart.
public sealed record EnshroudedSteamRoot(string SteamUserId, string WorldsPath);

// LocalRoot is the game's "Local" save folder, the only one SaveHarbor writes to. It may not exist yet.
public sealed record EnshroudedSaveRoots(string LocalRoot, IReadOnlyList<EnshroudedSteamRoot> SteamRoots);

public interface IEnshroudedSaveRootLocator
{
    EnshroudedSaveRoots Locate();
}
