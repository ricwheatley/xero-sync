using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using XeroSync.Worker.Services;  // for TokenStore and TokenInfo

namespace XeroSync.Worker.Services
{
    // Holds your client credentials (ClientId, Secret, RedirectUri)
    public record ClientConfig(string ClientId, string ClientSecret, string RedirectUri);

    public class XeroTokenService
    {
        private readonly HttpClient _http = new HttpClient();
        private readonly ClientConfig _cfg;

        public XeroTokenService()
        {
            // Load client.json from disk (un‑encrypted)
            // Format it as:
            // {
            //   "ClientId":    "...",
            //   "ClientSecret":"...",
            //   "RedirectUri": "http://localhost:5000/callback"
            // }
            var cfgJson = File.ReadAllText(Path.Combine("config", "client.json"));
            _cfg = JsonSerializer.Deserialize<ClientConfig>(cfgJson)
                   ?? throw new Exception("Failed to load client.json");
        }

        /// <summary>
        /// Exchange an auth code for initial tokens.
        /// Call once manually (and save via TokenStore.Save).
        /// </summary>
        public async Task<TokenInfo> ExchangeCodeAsync(string authCode)
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type",    "authorization_code"),
                new KeyValuePair<string,string>("client_id",     _cfg.ClientId),
                new KeyValuePair<string,string>("client_secret", _cfg.ClientSecret),
                new KeyValuePair<string,string>("code",          authCode),
                new KeyValuePair<string,string>("redirect_uri",  _cfg.RedirectUri),
            });

            var resp = await _http.PostAsync("https://identity.xero.com/connect/token", body);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            var token = JsonSerializer.Deserialize<TokenInfo>(json);
            return token ?? throw new Exception("Invalid token response");
        }

        /// <summary>
        /// Refreshes the access token using a stored refresh token.
        /// </summary>
        public async Task<TokenInfo> RefreshAsync(string currentRefreshToken)
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type",    "refresh_token"),
                new KeyValuePair<string,string>("client_id",     _cfg.ClientId),
                new KeyValuePair<string,string>("client_secret", _cfg.ClientSecret),
                new KeyValuePair<string,string>("refresh_token", currentRefreshToken),
            });

            var resp = await _http.PostAsync("https://identity.xero.com/connect/token", body);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync();
            var token = JsonSerializer.Deserialize<TokenInfo>(json);
            return token ?? throw new Exception("Invalid token response");
        }
    }
}
