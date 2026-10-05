using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure;

public sealed class PlayerIdentity(IAppSettingsStore settings) : IPlayerIdentity
{
    public const int MaxLength = 32;

    public string DisplayName => Normalize(settings.Current.PlayerName) is { Length: > 0 } name ? name : Environment.UserName;

    public static string Normalize(string? name)
    {
        var cleaned = new string((name ?? string.Empty).Where(character => !char.IsControl(character)).ToArray()).Trim();
        return cleaned.Length > MaxLength ? cleaned[..MaxLength].Trim() : cleaned;
    }
}
