using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games;

// Built-in per-game values that options are read on top of. The one table of game constants outside the game folders,
// so the options loader does not have to know the games.
internal static class GameDefaults
{
    public static string LaunchUri(GameId game) => game switch
    {
        GameId.Windrose => "steam://rungameid/3041230",
        GameId.Dragonwilds => "steam://rungameid/1374490",
        GameId.Enshrouded => "steam://rungameid/1203620",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };
}
