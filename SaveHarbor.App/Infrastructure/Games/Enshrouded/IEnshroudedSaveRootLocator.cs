namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// SteamUserId is the numeric userdata folder name; it is only used to tell several Steam accounts apart.
public sealed record EnshroudedSteamRoot(string SteamUserId, string WorldsPath);

// The one folder SaveHarbor manages: the game's Steam Cloud folder of the signed-in Steam account ("SAVEGAMES:STEAM"),
// or the folder chosen in Settings. Steam backs that folder up after every session. IsSteamCloud is false for a chosen
// folder.
public sealed record EnshroudedSaveRoot(string WorldsPath, bool IsSteamCloud);

public interface IEnshroudedSaveRootLocator
{
    // Null while no Steam account on this PC has started the game yet.
    EnshroudedSaveRoot? Locate();
}
