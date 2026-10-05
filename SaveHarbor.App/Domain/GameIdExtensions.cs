namespace SaveHarbor.App.Domain;

public static class GameIdExtensions
{
    public static string ToStorageKey(this GameId game) => game switch
    {
        GameId.Windrose => "windrose",
        GameId.Dragonwilds => "dragonwilds",
        _ => throw new ArgumentOutOfRangeException(nameof(game), game, null)
    };

    public static bool TryParseStorageKey(string? value, out GameId game)
    {
        foreach (var candidate in Enum.GetValues<GameId>())
        {
            if (string.Equals(candidate.ToStorageKey(), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                game = candidate;
                return true;
            }
        }

        game = default;
        return false;
    }
}
