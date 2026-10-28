using System.IO;
using Microsoft.Win32;
using SaveHarbor.App.Domain;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// The Local folder under Saved Games, plus one Steam Cloud folder per Steam account that has played the game. A save
// folder chosen in Settings (or SaveRoot in appsettings.json) replaces all of them with that one Local folder.
public sealed class EnshroudedSaveRootLocator(GameOptionsProvider optionsProvider) : IEnshroudedSaveRootLocator
{
    private const string SteamAppId = "1203620";

    public EnshroudedSaveRoots Locate()
    {
        var overrideRoot = optionsProvider.Get(GameId.Enshrouded).SaveRoot;
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            return new EnshroudedSaveRoots(Environment.ExpandEnvironmentVariables(overrideRoot), []);
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localRoot = Path.Combine(userProfile, "Saved Games", "keen games", "Enshrouded", "default");
        return new EnshroudedSaveRoots(localRoot, FindSteamRoots(ReadSteamPath()));
    }

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
