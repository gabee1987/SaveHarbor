using System.IO;
using Microsoft.Win32;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// The Steam Cloud folder of the Steam account that is signed in (or, with Steam closed, the account that played last).
// A save folder chosen in Settings (or SaveRoot in appsettings.json) replaces it. The game's Local tab is not managed:
// one folder per PC keeps every world to one copy, and Steam Cloud keeps its own backup of it.
public sealed class EnshroudedSaveRootLocator(GameOptionsProvider optionsProvider) : IEnshroudedSaveRootLocator
{
    private const string SteamAppId = "1203620";

    public EnshroudedSaveRoot? Locate()
    {
        var overrideRoot = optionsProvider.Get(GameId.Enshrouded).SaveRoot;
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new EnshroudedSaveRoot(Environment.ExpandEnvironmentVariables(overrideRoot), IsSteamCloud: false);
        }

        return ChooseSteamRoot(FindSteamRoots(ReadSteamPath()), ReadActiveSteamUserId()) is { } steam
            ? new EnshroudedSaveRoot(steam.WorldsPath, IsSteamCloud: true)
            : null;
    }

    // The signed-in account when it has played the game; otherwise the account whose save folder changed last.
    public static EnshroudedSteamRoot? ChooseSteamRoot(IReadOnlyList<EnshroudedSteamRoot> roots, string? activeSteamUserId) =>
        roots.FirstOrDefault(root => string.Equals(root.SteamUserId, activeSteamUserId, StringComparison.Ordinal))
        ?? roots.OrderByDescending(root => Directory.GetLastWriteTimeUtc(root.WorldsPath)).FirstOrDefault();

    public static IReadOnlyList<EnshroudedSteamRoot> FindSteamRoots(string? steamPath)
    {
        var userData = string.IsNullOrWhiteSpace(steamPath) ? null : Path.Combine(steamPath, "userdata");
        if (userData is null || !Directory.Exists(userData))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(userData)
                .Select(folder => (Id: Path.GetFileName(folder), Remote: Path.Combine(folder, SteamAppId, "remote")))
                .Where(entry => entry.Id.Length > 0 && entry.Id.All(char.IsAsciiDigit) && Directory.Exists(entry.Remote))
                .OrderBy(entry => entry.Id, StringComparer.Ordinal)
                .Select(entry => new EnshroudedSteamRoot(entry.Id, entry.Remote))
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Steam keeps the signed-in account's id (the userdata folder name) here while it runs; 0 when nobody is signed in.
    private static string? ReadActiveSteamUserId()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam\ActiveProcess", "ActiveUser", null) is int id and not 0
                ? ((uint)id).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : null;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadSteamPath()
    {
        try
        {
            return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string
                ?? Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
