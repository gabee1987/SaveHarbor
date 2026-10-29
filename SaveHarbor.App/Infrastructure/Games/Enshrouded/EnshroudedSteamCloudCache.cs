using System.IO;
using System.Text;

namespace SaveHarbor.App.Infrastructure.Games.Enshrouded;

// Steam lists every file it keeps in Steam Cloud for the game in "remotecache.vdf", next to the "remote" folder. A world
// file missing from that list is unknown to Steam: the game does not show it, and Steam does not bring it back once it
// is deleted (observed 2026-10-29, after a world deleted in the game was downloaded into its old slot). Read-only;
// SaveHarbor never writes the list. Anything it cannot read for certain counts as "unknown", never as "not tracked".
public static class EnshroudedSteamCloudCache
{
    public const string FileName = "remotecache.vdf";
    private const long MaxBytes = 4 * 1024 * 1024;

    // True when Steam tracks the world file, false when the list was read and does not name it, null when there is no
    // readable list (not a Steam Cloud folder, or the list could not be read or parsed).
    public static bool? IsTracked(string worldPath)
    {
        var folder = Path.GetDirectoryName(worldPath);
        var appFolder = string.IsNullOrEmpty(folder) ? null : Path.GetDirectoryName(folder);
        if (string.IsNullOrEmpty(appFolder))
        {
            return null;
        }

        try
        {
            var cache = new FileInfo(Path.Combine(appFolder, FileName));
            if (!cache.Exists || cache.Length > MaxBytes)
            {
                return null;
            }

            using var stream = new FileStream(cache.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return TryListFiles(reader.ReadToEnd()) is { } files ? files.Contains(Path.GetFileName(worldPath)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // The names of the blocks one level inside the root block: "<app id>" { "ChangeNumber" "…" "<file>" { … } … }.
    // Returns null for anything that is not exactly that shape of quoted keys, values and braces.
    public static IReadOnlySet<string>? TryListFiles(string text)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var depth = 0;
        var roots = 0;
        string? key = null;
        var index = 0;
        while (index < text.Length)
        {
            var c = text[index];
            if (char.IsWhiteSpace(c) || c == '﻿')
            {
                index++;
            }
            else if (c == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                index = text.IndexOf('\n', index) is var end and >= 0 ? end + 1 : text.Length;
            }
            else if (c == '{')
            {
                if (key is null)
                {
                    return null;
                }

                if (depth == 0)
                {
                    roots++;
                }
                else if (depth == 1)
                {
                    files.Add(key);
                }

                depth++;
                key = null;
                index++;
            }
            else if (c == '}')
            {
                if (depth == 0 || key is not null)
                {
                    return null;
                }

                depth--;
                index++;
            }
            else if (c == '"' && TryReadQuoted(text, ref index) is { } value)
            {
                // A string is a key, or the value of the key before it.
                key = key is null ? value : null;
            }
            else
            {
                return null;
            }
        }

        return depth == 0 && roots == 1 && key is null ? files : null;
    }

    private static string? TryReadQuoted(string text, ref int index)
    {
        var builder = new StringBuilder();
        for (var i = index + 1; i < text.Length; i++)
        {
            var c = text[i];
            if (c == '"')
            {
                index = i + 1;
                return builder.ToString();
            }

            if (c == '\\' && i + 1 < text.Length)
            {
                c = text[++i];
            }
            else if (c is '\n' or '\r')
            {
                return null;
            }

            builder.Append(c);
        }

        return null;
    }
}
