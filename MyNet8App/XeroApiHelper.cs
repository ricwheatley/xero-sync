using RestSharp;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

public class XeroConnection
{
    // Matches the JSON returned by GET /connections
    public string? id        { get; set; }
    public string? tenantId  { get; set; }
    public string? tenantType{ get; set; }
}

public static class XeroApiHelper
{
    /// <summary>
    /// Calls GET https://api.xero.com/connections
    /// to retrieve the list of tenants connected to your app.
    /// </summary>
    public static async Task<List<XeroConnection>> GetConnectionsAsync(string accessToken)
    {
        var client  = new RestClient("https://api.xero.com/connections");
        var request = new RestRequest(string.Empty, Method.Get);
        request.AddHeader("Authorization", $"Bearer {accessToken}");

        var response = await client.ExecuteAsync(request);
        if (!response.IsSuccessful)
            throw new Exception($"Failed to get connections: {response.StatusCode} – {response.Content}");

        // Parse into a List<XeroConnection>
        return JsonSerializer.Deserialize<List<XeroConnection>>(
            response.Content ?? throw new Exception("Empty connections response"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
        );
    }
}
