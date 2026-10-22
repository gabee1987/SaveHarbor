using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using SaveHarbor.App.Domain;
using SaveHarbor.App.Services;

namespace SaveHarbor.App.Infrastructure.Cloud.Supabase;

// The signed-in SaveHarbor Cloud session. ToString leaves the tokens out, so a session never ends up in a log.
public sealed record SupabaseSession(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAtUtc, string Email, string UserId)
{
    public override string ToString() => $"SupabaseSession {{ UserId = {UserId}, ExpiresAtUtc = {ExpiresAtUtc:O} }}";
}

// Keeps the session in one file encrypted with DPAPI for the current Windows user: another account on the PC, or a
// copy of the file on another PC, cannot read it. A file that cannot be read counts as signed out.
public sealed class SupabaseSessionStore(IAppDataPathProvider paths, IAppLogger logger)
{
    private static readonly byte[] Entropy = "SaveHarbor.SupabaseSession.v1"u8.ToArray();

    public SupabaseSession? Load()
    {
        var path = paths.SupabaseSessionPath;
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            var session = JsonSerializer.Deserialize<SupabaseSession>(plain);
            if (session is null || string.IsNullOrEmpty(session.AccessToken) || string.IsNullOrEmpty(session.RefreshToken))
            {
                throw new InvalidDataException("The saved session is incomplete.");
            }

            return session;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException or UnauthorizedAccessException)
        {
            logger.Warning(AppLogKeyword.CloudAuth, "The saved SaveHarbor Cloud sign-in could not be read ({Error}); sign in again", exception.GetType().Name);
            return null;
        }
    }

    public void Save(SupabaseSession session)
    {
        var path = paths.SupabaseSessionPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var encrypted = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(session), Entropy, DataProtectionScope.CurrentUser);

        var tempPath = path + ".tmp";
        File.WriteAllBytes(tempPath, encrypted);
        if (File.Exists(path))
        {
            // A hidden file cannot be replaced until its attributes are cleared.
            File.SetAttributes(path, FileAttributes.Normal);
        }

        File.Move(tempPath, path, overwrite: true);
        File.SetAttributes(path, FileAttributes.Hidden | FileAttributes.NotContentIndexed);
    }

    public void Clear()
    {
        var path = paths.SupabaseSessionPath;
        if (File.Exists(path))
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }
}
