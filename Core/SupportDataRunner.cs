using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace XeroSync.Worker.Core;

public sealed class SupportDataRunner : ISupportDataRunner
{
    private static readonly List<string> StandardEndpoints = new()
    {
        "Invoices", "Contacts", "Accounts", "BankTransactions",
        "TrackingCategories", "CreditNotes", "Payments", "PurchaseOrders"
    };

    public async Task RunAsync(SqlConnection sqlConn, Guid tenantId, string accessToken, CancellationToken ct)
    {
        Console.WriteLine("\n🚀 Starting Standard Endpoint Sync...");

        foreach (var endpoint in StandardEndpoints)
        {
            await SyncEndpointAsync(sqlConn, endpoint, tenantId, accessToken, ct);
            await Task.Delay(500, ct); // Be kind to Xero API
        }
    }

    private async Task SyncEndpointAsync(SqlConnection sqlConn, string endpoint, Guid tenantId, string accessToken, CancellationToken ct)
    {
        Console.WriteLine($"\n📬 Syncing {endpoint}...");

        var lastSync = await GetLastSyncTimeAsync(sqlConn, tenantId, endpoint);
        string? aggregatedJsonArray = await FetchPaginatedDataAsync(endpoint, accessToken, tenantId.ToString(), lastSync);

        if (aggregatedJsonArray == null)
        {
            Console.WriteLine($"❌ Fetch failed for {endpoint}.");
            return;
        }

        if (aggregatedJsonArray == "[]")
        {
            Console.WriteLine($"✅ No new data for {endpoint}.");
            return;
        }

        int rows = await InsertJsonAsync(sqlConn, endpoint, aggregatedJsonArray, tenantId);
        Console.WriteLine($"💾 Inserted {rows} rows into XeroRaw_{endpoint}.");

        if (rows > 0)
        {
            await ProcessRawDataAsync(sqlConn, $"Process{endpoint}Raw");
            await UpdateLastSyncTimeAsync(sqlConn, tenantId, endpoint, DateTime.UtcNow);
        }
    }

    private async Task<string?> FetchPaginatedDataAsync(string endpoint, string token, string tenantId, DateTime? since)
    {
        var allItems = new List<JsonNode>();
        int page = 1;
        int totalPages = 1;

        var client = new RestSharp.RestClient("https://api.xero.com/api.xro/2.0/");

        while (page <= totalPages)
        {
            var req = new RestSharp.RestRequest(endpoint, RestSharp.Method.Get)
                .AddParameter("page", page)
                .AddHeader("Authorization", $"Bearer {token}")
                .AddHeader("xero-tenant-id", tenantId)
                .AddHeader("Accept", "application/json");

            if (since.HasValue)
                req.AddHeader("If-Modified-Since", since.Value.ToUniversalTime().ToString("R"));

            var res = await client.ExecuteAsync(req);

            if (!res.IsSuccessful)
            {
                Console.WriteLine($"❌ {endpoint} page {page}: {res.StatusCode} – {res.ErrorMessage}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(res.Content))
            {
                Console.WriteLine($"⚠️ {endpoint} page {page}: empty content.");
                break;
            }

            var json = JsonNode.Parse(res.Content);
            var data = json?[endpoint] as JsonArray;
            if (data is null)
            {
                Console.WriteLine($"⚠️ {endpoint}: expected array not found.");
                break;
            }

            allItems.AddRange(data);
            totalPages = json["pagination"]?["pageCount"]?.GetValue<int>() ?? 1;
            page++;
        }

        return new JsonArray(allItems.ToArray()).ToJsonString();
    }

    private async Task<int> InsertJsonAsync(SqlConnection conn, string endpoint, string json, Guid tenantId)
    {
        string sql = $"INSERT INTO dbo.XeroRaw_{endpoint} (TenantGuid, JsonBody) VALUES (@TenantGuid, @JsonBody)";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
        cmd.Parameters.AddWithValue("@JsonBody", json);
        return await cmd.ExecuteNonQueryAsync();
    }

    private async Task ProcessRawDataAsync(SqlConnection conn, string procName)
    {
        Console.WriteLine($"🔨 Executing {procName}...");
        await using var cmd = new SqlCommand(procName, conn)
        {
            CommandType = System.Data.CommandType.StoredProcedure,
            CommandTimeout = 300
        };
        await cmd.ExecuteNonQueryAsync();
    }

    private async Task<DateTime?> GetLastSyncTimeAsync(SqlConnection conn, Guid tenantId, string endpoint)
    {
        string sql = "SELECT MAX(LastUtc) FROM dbo.XeroSyncPoint WHERE TenantGuid = @Tenant AND Endpoint = @Endpoint";
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Tenant", tenantId);
        cmd.Parameters.AddWithValue("@Endpoint", endpoint);
        var result = await cmd.ExecuteScalarAsync();
        return result != DBNull.Value && result is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;
    }

    private async Task UpdateLastSyncTimeAsync(SqlConnection conn, Guid tenantId, string endpoint, DateTime timestamp)
    {
        string sql = """
            MERGE dbo.XeroSyncPoint AS tgt
            USING (SELECT @Tenant AS TenantGuid, @Endpoint AS Endpoint, @LastUtc AS LastUtc) AS src
            ON tgt.TenantGuid = src.TenantGuid AND tgt.Endpoint = src.Endpoint
            WHEN MATCHED THEN UPDATE SET LastUtc = src.LastUtc
            WHEN NOT MATCHED THEN INSERT (TenantGuid, Endpoint, LastUtc)
            VALUES (src.TenantGuid, src.Endpoint, src.LastUtc);
            """;

        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@Tenant", tenantId);
        cmd.Parameters.AddWithValue("@Endpoint", endpoint);
        cmd.Parameters.AddWithValue("@LastUtc", timestamp);
        await cmd.ExecuteNonQueryAsync();
    }
}
