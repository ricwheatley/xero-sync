using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using XeroSync.Worker.Services;

namespace XeroSync.Worker
{
    public static class TokenHelper
    {
        private record ClientSecrets(string Id, string Secret, string? BootstrapRefresh);

        private static readonly Lazy<ClientSecrets> _client = new(() =>
        {
            // 1️⃣ Prefer CI / environment variables (handy in GitHub Actions / Azure DevOps)
            var id       = Environment.GetEnvironmentVariable("XERO_CLIENT_ID");
            var secret   = Environment.GetEnvironmentVariable("XERO_CLIENT_SECRET");
            var bootstrap= Environment.GetEnvironmentVariable("XERO_REFRESH_TOKEN");
            if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(secret))
                return new ClientSecrets(id, secret, bootstrap);

            // 2️⃣ Fallback to config/client.json  ─ supports either snake_case or PascalCase keys
            var jsonPath = Path.Combine("config", "client.json");
            var doc      = JsonNode.Parse(File.ReadAllText(jsonPath))
                           ?? throw new Exception("Invalid client.json");

            string? get(string snake, string pascal)
                => doc[snake]?.GetValue<string>() ?? doc[pascal]?.GetValue<string>();

            return new ClientSecrets(
                get("client_id",     "ClientId")     ?? throw new Exception("client_id (or ClientId) missing in client.json"),
                get("client_secret", "ClientSecret") ?? throw new Exception("client_secret (or ClientSecret) missing in client.json"),
                get("refresh_token", "RefreshToken")
            );
        });

        private static string ClientId        => _client.Value.Id;
        private static string ClientSecret    => _client.Value.Secret;
        private static string? BootstrapToken => _client.Value.BootstrapRefresh;

        private const string TokenEndpoint = "https://identity.xero.com/connect/token";

        public static async Task<string> AcquireTokenAsync()
        {
            // 1️⃣ reuse cached token.dat if still valid
            try
            {
                var existing = TokenStore.Load();
                if (DateTime.UtcNow < existing.obtained_at.AddSeconds(existing.expires_in - 120))
                    return existing.access_token;

                // otherwise refresh with stored refresh_token
                return await ExchangeRefreshAsync(existing.refresh_token);
            }
            catch
            {
                // swallow – will try bootstrap next
            }

            // 2️⃣ bootstrap with env‑var OR refresh_token inside client.json
            var bootstrap = BootstrapToken
                             ?? Environment.GetEnvironmentVariable("XERO_REFRESH_TOKEN");

            if (string.IsNullOrWhiteSpace(bootstrap))
                throw new Exception("No cached token.dat and no bootstrap refresh token found. Either set XERO_REFRESH_TOKEN env‑var or add \"refresh_token\" (or RefreshToken) to config/client.json");

            return await ExchangeRefreshAsync(bootstrap);
        }

        /// <summary>Manual exchange if caller already holds a fresh refresh_token.</summary>
        public static Task<string> AcquireTokenAsync(string refreshToken) => ExchangeRefreshAsync(refreshToken);

        // ───────────────────────────────────────────────────────────────
        private static async Task<string> ExchangeRefreshAsync(string refreshToken)
        {
            using var client = new HttpClient();
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type",    "refresh_token"),
                new KeyValuePair<string,string>("refresh_token", refreshToken),
                new KeyValuePair<string,string>("client_id",     ClientId),
                new KeyValuePair<string,string>("client_secret", ClientSecret),
            });

            var resp = await client.PostAsync(TokenEndpoint, body);
            resp.EnsureSuccessStatusCode();

            var json      = await resp.Content.ReadAsStringAsync();
            var tokenInfo = JsonSerializer.Deserialize<TokenInfo>(json)
                            ?? throw new Exception("Failed to parse token JSON");

            TokenStore.Save(tokenInfo);
            return tokenInfo.access_token;
        }
    }

    public static class TenantHelper
    {
        private const string ConnectionsEndpoint = "https://api.xero.com/connections";
        public static async Task<Guid> DiscoverTenantAsync(string accessToken)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            var resp = await client.GetAsync(ConnectionsEndpoint);
            resp.EnsureSuccessStatusCode();

            var array = JsonSerializer.Deserialize<JsonArray>(await resp.Content.ReadAsStringAsync())
                        ?? throw new Exception("Failed to parse connections array");

            return array[0]? ["tenantId"]?.GetValue<Guid>()
                   ?? throw new Exception("No tenantId found in connections");
        }
    }

    public static class Configuration
    {
        public static string GetConnectionString(string _) =>
            Environment.GetEnvironmentVariable("XERO_SQL_CONN")
            ?? throw new Exception("Environment variable XERO_SQL_CONN not set");
    }
}
