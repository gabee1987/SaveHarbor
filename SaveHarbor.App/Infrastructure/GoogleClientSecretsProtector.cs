using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SaveHarbor.App.Infrastructure;

public static class GoogleClientSecretsProtector
{
    public const string EncryptedFileName = "google-client-secret.enc";
    public const string PlainFileName = "google-client-secret.json";

    private const string KeyMaterial = "SaveHarbor.GoogleOAuthClientSecrets.v1";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static Stream OpenRead(string path)
    {
        if (!path.EndsWith(".enc", StringComparison.OrdinalIgnoreCase))
        {
            return File.OpenRead(path);
        }

        var encrypted = JsonSerializer.Deserialize<EncryptedClientSecretsFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidDataException("Encrypted Google client secrets file is empty.");

        if (!string.Equals(encrypted.Algorithm, "AES-256-GCM", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Encrypted Google client secrets file uses an unsupported algorithm.");
        }

        var nonce = Convert.FromBase64String(encrypted.Nonce);
        var tag = Convert.FromBase64String(encrypted.Tag);
        var ciphertext = Convert.FromBase64String(encrypted.Ciphertext);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(CreateKey(), tag.Length);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);

        return new MemoryStream(plaintext, writable: false);
    }

    private static byte[] CreateKey()
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(KeyMaterial));
    }

    private sealed class EncryptedClientSecretsFile
    {
        public int Version { get; set; } = 1;

        public string Algorithm { get; set; } = string.Empty;

        public string Nonce { get; set; } = string.Empty;

        public string Tag { get; set; } = string.Empty;

        public string Ciphertext { get; set; } = string.Empty;
    }
}
