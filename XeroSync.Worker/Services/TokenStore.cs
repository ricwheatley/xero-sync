using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace XeroSync.Worker.Services
{
    // Represents the JSON payload you’ll store (and re‑store) on disk
    public record TokenInfo
    {
        public string access_token  { get; init; } = default!;
        public string refresh_token { get; init; } = default!;
        public int    expires_in    { get; init; }
        public DateTime obtained_at { get; init; }
    }

    public static class TokenStore
    {
        // Path relative to your app’s working directory
        private static readonly string TokenFilePath = Path.Combine("config", "token.dat");

        public static TokenInfo Load()
        {
            if (!File.Exists(TokenFilePath))
                throw new FileNotFoundException($"Token file not found: {TokenFilePath}");

            // Read & decrypt
            var encrypted = File.ReadAllBytes(TokenFilePath);
            #if WINDOWS
                var decrypted = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
                var json      = Encoding.UTF8.GetString(decrypted);

                return JsonSerializer.Deserialize<TokenInfo>(json)
                ?? throw new Exception("Failed to parse token JSON");
            #else
                // For non-Windows platforms, you might need to use a different method for encryption/decryption.
                throw new NotSupportedException("Token decryption is not supported on this platform.");
            #endif
        }

        public static void Save(TokenInfo token)
        {
            // Serialize with timestamp
            var toStore = token with { obtained_at = DateTime.UtcNow };
            var json    = JsonSerializer.Serialize(toStore);

            // Encrypt & write
            var bytes     = Encoding.UTF8.GetBytes(json);
            #if WINDOWS
                var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(TokenFilePath)!);
                File.WriteAllBytes(TokenFilePath, encrypted);
            #else
                // For non-Windows platforms, you might need to use a different method for encryption/decryption.
                throw new NotSupportedException("Token encryption is not supported on this platform.");
            #endif
        }
    }
}
