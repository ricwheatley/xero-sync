using RestSharp;
using System;
using System.Text.Json;
using System.Threading.Tasks;

public static class XeroAuthHelper
{
    private static readonly string TokenUrl     = "https://identity.xero.com/connect/token";
    private static string ClientId             = ConfigurationHelper.Get("Xero:ClientId");
    private static string ClientSecret         = ConfigurationHelper.Get("Xero:ClientSecret");
    private static string RefreshToken         = ConfigurationHelper.Get("Xero:RefreshToken");

    /// <summary>
    /// Calls Xero’s token endpoint with the stored refresh token
    /// and returns a fresh access token.
    /// </summary>
    public static async Task<string> GetAccessTokenAsync()
    {
        // 1. Create the REST client and request
        var client  = new RestClient(TokenUrl);
        // RestRequest requires a “resource” string; for a direct URL-based client, pass empty.
        var request = new RestRequest(string.Empty, Method.Post);

        // 2. Add form data for OAuth2 refresh
        request.AddHeader("Content-Type", "application/x-www-form-urlencoded");
        request.AddParameter("grant_type",    "refresh_token");
        request.AddParameter("client_id",     ClientId);
        request.AddParameter("client_secret", ClientSecret);
        request.AddParameter("refresh_token", RefreshToken);

        // 3. Execute the request
        var response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful)
            throw new Exception($"Error fetching access token: {response.StatusCode} – {response.Content}");

        // 4. Parse the JSON response
        using var doc  = JsonDocument.Parse(response.Content ?? throw new Exception("Empty token response"));
        var root       = doc.RootElement;
        var accessToken= root.GetProperty("access_token").GetString() 
                           ?? throw new Exception("No access_token in response");
        var newRefresh = root.GetProperty("refresh_token").GetString()
                           ?? throw new Exception("No refresh_token in response");

        // 5. (Optional) Persist the new refresh token if you want:
        // ConfigurationHelper.Update("Xero:RefreshToken", newRefresh);

        return accessToken;
    }
}
