using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net; // Required for HttpStatusCode
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using MyNet8App.Services; // Assuming XeroReportFetcher namespace
using RestSharp;
// Add using statements for your actual helper class namespaces if they differ

// --- Helper classes for JSON Deserialization ---
// NOTE: These MUST be defined *outside* the Program class if public,
// or *inside* if only used by Program (less common for reusable models).
// Let's define them outside for potential reusability.

public class XeroPagination
{
    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }

    [JsonPropertyName("pageCount")]
    public int PageCount { get; set; }

    [JsonPropertyName("itemCount")]
    public int ItemCount { get; set; }
}

public class XeroApiResponse<T>
{
    [JsonPropertyName("pagination")]
    public XeroPagination? Pagination { get; set; }

    public JsonNode? DataNode { get; set; }

    public List<T> GetItems(string nodeName)
    {
        if (DataNode != null && DataNode[nodeName] is JsonArray jsonArray)
        {
            try
            {
                return JsonSerializer.Deserialize<List<T>>(jsonArray.ToJsonString()) ?? new List<T>();
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"❌ Error deserializing items for node '{nodeName}': {ex.Message}");
                return new List<T>();
            }
        }
        return new List<T>();
    }
}

// --- Main Program Class ---
class Program
{
    // --- Constants and Static Fields ---
    private const string XeroApiBaseUrl = "https://api.xero.com/api.xro/2.0/";
    private static readonly JsonSerializerOptions _jsonOptions = new()
        { PropertyNameCaseInsensitive = true };

    // --- Main Entry Point ---
    static async Task Main(string[] args)
    {
        // 1. Acquire Token
        TokenInfo token;
        var tokenSvc = new XeroTokenService(); // Assuming this exists
        try { token = await GetValidTokenAsync(tokenSvc); }
        catch (Exception ex) { Console.WriteLine($"❌ Token Error: {ex.Message}"); return; }

        // 2. Discover Tenant
        string? tenantIdStr;
        Guid tenantId;
        try { tenantIdStr = await DiscoverTenantAsync(token.access_token); } // Assuming XeroApiHelper exists
        catch (Exception ex) { Console.WriteLine($"❌ Tenant Discovery Error: {ex.Message}"); return; }
        if (!Guid.TryParse(tenantIdStr, out tenantId) || tenantId == Guid.Empty)
        { Console.WriteLine("❌ Invalid Tenant ID discovered."); return; }
        Console.WriteLine($"✅ Using TenantGuid: {tenantId}");

        // 3. Get SQL Connection String
        string connStr = "";
        try { connStr = ConfigurationHelper.Get("ConnectionStrings:XeroPOC"); } // Assuming this exists
        catch (Exception ex) { Console.WriteLine($"❌ Configuration error: {ex.Message}"); return; }
        if (string.IsNullOrEmpty(connStr)) { Console.WriteLine("❌ DB Connection string not found."); return; }


        // --- List of Endpoints to Sync ---
        var standardEndpoints = new List<string>
        {
            "Invoices", "Contacts", "Accounts", "BankTransactions",
            "TrackingCategories", "CreditNotes", "Payments", "PurchaseOrders"
        };

        // --- Main Sync Process ---
        try
        {
             await using var sqlConn = new SqlConnection(connStr);
             await sqlConn.OpenAsync();
             Console.WriteLine("✅ Database connection opened.");

            // --- Ingest Standard Endpoints ---
            Console.WriteLine("\n🚀 Starting Standard Endpoint Sync...");
            foreach (var endpoint in standardEndpoints)
            {
                bool success = false;
                DateTime? lastSyncTime = null;
                try
                {
                    lastSyncTime = await GetLastSyncTimeAsync(sqlConn, tenantId, endpoint);
                    success = await IngestDataAsync(sqlConn, endpoint, tenantId, token.access_token, lastSyncTime);

                    if (success)
                    {
                        // Update timestamp only if IngestDataAsync reported success (data processed or no new data needed)
                        await UpdateLastSyncTimeAsync(sqlConn, tenantId, endpoint, DateTime.UtcNow);
                        Console.WriteLine($"✅ Timestamp updated for {endpoint}.");
                    }
                    else
                    {
                        Console.WriteLine($"ℹ️ Sync failed or incomplete for {endpoint}. Timestamp not updated.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌❌ FATAL Error processing endpoint {endpoint}: {ex.Message}");
                }
                 Console.WriteLine("--------------------------------------------------");
                 await Task.Delay(500); // Be kind to API
            }

            // --- Fetch Reports ---
            Console.WriteLine("\n🚀 Starting Report Fetching...");
            await FetchAndProcessReportsAsync(sqlConn, connStr, tenantId, token.access_token, args);

        }
        catch (SqlException sqlEx) { Console.WriteLine($"❌ Database error: {sqlEx.Message}"); }
        catch (Exception ex) { Console.WriteLine($"❌ An unexpected error occurred: {ex.Message}"); }
        finally { Console.WriteLine("\n✅ Process finished."); }
    }

    // ==================================================================
    //          TOKEN & TENANT HELPERS (Inside Program class)
    // ==================================================================
     private static async Task<TokenInfo> GetValidTokenAsync(XeroTokenService tokenSvc)
     {
         // Assuming TokenStore and TokenInfo classes exist and work
         TokenInfo token;
         try { token = TokenStore.Load(); }
         catch (FileNotFoundException)
         {
             Console.WriteLine("🔑 No token found, please run the auth‑code flow first.");
             throw new InvalidOperationException("Token file not found."); // More specific exception
         }

         var age = DateTime.UtcNow - token.obtained_at;
         if (age.TotalSeconds >= token.expires_in - 60) // Refresh if < 1 min left
         {
             Console.WriteLine("🔄 Refreshing access token...");
             token = await tokenSvc.RefreshAsync(token.refresh_token);
             TokenStore.Save(token);
         } else { Console.WriteLine("✅ Using cached access token."); }
         return token;
     }

     private static async Task<string?> DiscoverTenantAsync(string accessToken)
     {
         // Assuming XeroApiHelper and ConnectionInfo classes exist and work
         var connections = await XeroApiHelper.GetConnectionsAsync(accessToken);
         if (connections == null || connections.Count == 0)
         {
             throw new InvalidOperationException("❌ No tenants found or error fetching connections.");
         }
         var tenantId = connections[0].tenantId; // Assuming first tenant
         if (string.IsNullOrEmpty(tenantId))
         {
             throw new InvalidOperationException("❌ Discovered tenant ID is invalid.");
         }
         return tenantId;
     }

    // ==================================================================
    //          DATA INGESTION (Option B - Aggregated JSON)
    // ==================================================================
    private static async Task<bool> IngestDataAsync(SqlConnection sqlConn, string endpoint, Guid tenantId, string accessToken, DateTime? lastSyncTime)
    {
        Console.WriteLine($"\n📬 Syncing {endpoint}...");
        if (lastSyncTime.HasValue) Console.WriteLine($"    (Incremental sync since {lastSyncTime.Value:yyyy-MM-dd HH:mm:ss} UTC)");
        else Console.WriteLine("    (Full sync)");

        var rawTable = $"XeroRaw_{endpoint}";
        var procName = $"Process{endpoint}Raw";
        bool overallSuccess = false; // Track if entire process for endpoint is ok

        try
        {
            // Fetch and aggregate data from all pages into a single JSON array string
            // Returns null on critical fetch failure, "[]" on success with no data/304
            string? aggregatedJsonArray = await FetchPaginatedDataFromXeroAsync(endpoint, accessToken, tenantId.ToString(), lastSyncTime);

            if (aggregatedJsonArray == null)
            {
                Console.WriteLine($"    Fetch failed critically for {endpoint}.");
                return false; // Fetch failure
            }

            if (aggregatedJsonArray == "[]")
            {
                Console.WriteLine($"✅ No new/modified data found for {endpoint} (or 304 received).");
                return true; // Sync successful, nothing to process
            }

            Console.WriteLine($"  Aggregated data ({aggregatedJsonArray.Length:N0} chars) for {endpoint}.");

            // Insert the single aggregated JSON array string
            int rowsInserted = 0;
            try
            {
                rowsInserted = await InsertJsonToSqlAsync(sqlConn, rawTable, aggregatedJsonArray, tenantId);
                Console.WriteLine($"💾 Inserted {rowsInserted} row(s) into {rawTable}.");
            }
            catch (Exception dbEx)
            {
                Console.WriteLine($"❌ Error inserting aggregated data for {endpoint}: {dbEx.Message}");
                return false; // Failed insert
            }

            // Process the inserted aggregated data
            if (rowsInserted > 0)
            {
                try
                {
                     await ProcessDataAsync(sqlConn, procName);
                     overallSuccess = true; // Mark as successful
                }
                catch (Exception procEx)
                {
                    Console.WriteLine($"❌ Error processing aggregated data for {endpoint}: {procEx.Message}");
                    overallSuccess = false; // Processing failed
                }
            }
            else
            {
                 Console.WriteLine($"⚠️ No rows were inserted for {endpoint} (aggregated JSON might have been empty unexpectedly?).");
                 overallSuccess = true; // No insert needed, consider success
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error during ingestion process for {endpoint}: {ex.Message}");
            overallSuccess = false;
        }
        return overallSuccess;
    }

    // ==================================================================
    //          XERO API PAGINATED FETCH (Aggregating Data - Option B)
    // ==================================================================
     private static async Task<string?> FetchPaginatedDataFromXeroAsync(string endpoint, string accessToken, string tenantIdStr, DateTime? lastSyncTime)
     {
         var allItems = new List<JsonNode>();
         int currentPage = 1;
         bool isFirstPage = true;
         int totalPages = 1;

         var restClient = new RestClient(XeroApiBaseUrl);

         while (currentPage <= totalPages)
         {
             var request = new RestRequest(endpoint, Method.Get);
             request.AddParameter("page", currentPage, ParameterType.QueryString);
             request.AddHeader("Authorization", $"Bearer {accessToken}");
             request.AddHeader("xero-tenant-id", tenantIdStr);
             request.AddHeader("Accept", "application/json");

             if (lastSyncTime.HasValue)
             {
                 request.AddHeader("If-Modified-Since", lastSyncTime.Value.ToUniversalTime().ToString("R"));
             }

             Console.WriteLine($"    -> Calling {endpoint} | Page: {currentPage} of {totalPages} {(lastSyncTime.HasValue ? "| If-Modified-Since Used" : "")}");

             RestResponse response;
             try { response = await restClient.ExecuteAsync(request); }
             catch (Exception clientEx)
             {
                 Console.WriteLine($"❌ Exception during API call for {endpoint} page {currentPage}: {clientEx.Message}");
                 return null; // Indicate critical failure
             }

             // --- Handle Response Status Codes ---
             if (isFirstPage && lastSyncTime.HasValue && response.StatusCode == HttpStatusCode.NotModified)
             {
                 Console.WriteLine($"✅ {endpoint}: Not Modified (304).");
                 return "[]"; // Success, no data
             }
             // Handle 304 on subsequent pages - means no more changed data on later pages
             if (!isFirstPage && response.StatusCode == HttpStatusCode.NotModified)
             {
                 Console.WriteLine($"✅ {endpoint} Page {currentPage}: Not Modified (304). Assuming end of changed data.");
                 break; // Stop pagination
             }

             if (response.StatusCode == HttpStatusCode.TooManyRequests) // 429
             {
                 Console.WriteLine($"⚠️ {endpoint} page {currentPage}: Rate limit hit (429). Pausing...");
                 await Task.Delay(TimeSpan.FromSeconds(60));
                 continue; // Retry same page
             }

             if (!response.IsSuccessful)
             {
                 Console.WriteLine($"❌ {endpoint} page {currentPage} call failed: {response.StatusCode} – {response.ErrorMessage}");
                 return null; // Indicate critical failure
             }

             if (string.IsNullOrEmpty(response.Content))
             {
                 Console.WriteLine($"⚠️ {endpoint} page {currentPage}: Call successful but returned empty content. Stopping.");
                 break; // Stop if content is empty
             }

             // --- Parse JSON and Extract Data & Pagination Info ---
             try
             {
                 var jsonNode = JsonNode.Parse(response.Content);
                 if (jsonNode == null) throw new JsonException("Response content could not be parsed to JsonNode.");

                 var paginationNode = jsonNode["pagination"];
                 if (isFirstPage && paginationNode != null)
                 {
                     var pagination = JsonSerializer.Deserialize<XeroPagination>(paginationNode.ToJsonString(), _jsonOptions);
                     totalPages = pagination?.PageCount ?? 1; // Update total pages based on first response
                     Console.WriteLine($"    (Total Pages: {totalPages})");
                 }
                 else if (isFirstPage)
                 {
                     totalPages = 1; // Assume single page if no pagination info
                     Console.WriteLine("    (No pagination info found, assuming 1 page)");
                 }

                 var dataArrayNode = jsonNode[endpoint];
                 if (dataArrayNode is JsonArray dataArray)
                 {
                     int itemCount = dataArray.Count;
                     Console.WriteLine($"    <- Received page {currentPage} ({itemCount} items)");
                     allItems.AddRange(dataArray.Select(item => item!.DeepClone())); // Aggregate items
                     // Stop if we received fewer items than page size (might be last page)
                     // Note: Relies on standard page size being > 0 and consistent
                     // if (paginationNode != null) {
                     //    var pagination = JsonSerializer.Deserialize<XeroPagination>(paginationNode.ToJsonString(), _jsonOptions);
                     //    if (pagination != null && itemCount < pagination.PageSize) break;
                     // }
                 }
                 else
                 {
                      Console.WriteLine($"⚠️ {endpoint} page {currentPage}: Expected JSON array '{endpoint}' not found.");
                      break; // Stop if data structure is unexpected
                 }
             }
             catch (JsonException jsonEx)
             {
                 Console.WriteLine($"❌ Error parsing JSON for {endpoint} page {currentPage}: {jsonEx.Message}");
                 return null; // Indicate critical failure on parsing error
             }

             isFirstPage = false;
             currentPage++;
             if (currentPage <= totalPages) await Task.Delay(250); // Delay only if more pages expected
         }

         // Serialize the aggregated list back into a single JSON array string
         if (allItems.Count == 0) return "[]";

         var finalJsonArray = new JsonArray(allItems.ToArray());
         // Consider options for large JSON: JsonSerializerOptions { WriteIndented = false }
         return finalJsonArray.ToJsonString();
     }

    // ==================================================================
    //          REPORT FETCHING LOGIC (Inside Program class)
    // ==================================================================
     private static async Task FetchAndProcessReportsAsync(SqlConnection sqlConn, string connStr, Guid tenantId, string accessToken, string[] args)
     {
            // Date range logic
            DateTime financialYearStartDate = args.Length > 0 && DateTime.TryParseExact(args[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate) ? startDate : new DateTime(DateTime.Today.Year, 1, 1);
            DateTime targetMonthEndDate = args.Length > 1 && DateTime.TryParseExact(args[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var endDate) ? endDate : new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));
            Console.WriteLine($"🗓️ Report Date Range: {financialYearStartDate:yyyy-MM-dd} to {targetMonthEndDate:yyyy-MM-dd}");

            var fetcher = new XeroReportFetcher(connStr, accessToken, tenantId); // Ensure XeroReportFetcher class exists

            DateTime currentMonthIterator = new DateTime(financialYearStartDate.Year, financialYearStartDate.Month, 1);

            while (currentMonthIterator <= targetMonthEndDate)
            {
                DateTime currentMonthEndDate = new DateTime(currentMonthIterator.Year, currentMonthIterator.Month, DateTime.DaysInMonth(currentMonthIterator.Year, currentMonthIterator.Month));
                if (currentMonthEndDate > targetMonthEndDate) currentMonthEndDate = targetMonthEndDate;

                Console.WriteLine($"\n--- Processing reports for month ending: {currentMonthEndDate:yyyy-MM-dd} ---");

                var reportsToFetch = new List<(string ReportName, string QueryParams)>
                {
                    ("ProfitAndLoss", $"fromDate={currentMonthIterator:yyyy-MM-dd}&toDate={currentMonthEndDate:yyyy-MM-dd}"),
                    ("BalanceSheet", $"date={currentMonthEndDate:yyyy-MM-dd}"),
                    ("TrialBalance", $"date={currentMonthEndDate:yyyy-MM-dd}")
                };

                foreach (var reportInfo in reportsToFetch)
                {
                    bool success = false;
                    HttpStatusCode statusCode = HttpStatusCode.Unused; // Default
                    DateTime? lastSyncTime = null;
                    string reportSyncName = $"Report_{reportInfo.ReportName}";
                    try
                    {
                        lastSyncTime = await GetLastSyncTimeAsync(sqlConn, tenantId, reportSyncName);

                        // FetchReportAsync now returns success status and HTTP status code
                        (success, statusCode) = await fetcher.FetchReportAsync(reportInfo.ReportName, reportInfo.QueryParams, lastSyncTime);

                        // Update timestamp ONLY if fetch/process was successful (which includes 304 Not Modified)
                        if (success)
                        {
                            await UpdateLastSyncTimeAsync(sqlConn, tenantId, reportSyncName, DateTime.UtcNow);
                            if (statusCode != HttpStatusCode.NotModified) // Log success only if actual data was processed
                            {
                                Console.WriteLine($"✅ Successfully processed {reportInfo.ReportName} and updated timestamp.");
                            }
                            else
                            {
                                Console.WriteLine($"✅ Timestamp updated for {reportInfo.ReportName} (No changes detected).");
                            }
                        }
                        else { Console.WriteLine($"ℹ️ Report {reportInfo.ReportName} sync failed. Timestamp not updated."); }
                    }
                    catch (Exception reportEx) { Console.WriteLine($"❌ Error fetching/processing report {reportInfo.ReportName}: {reportEx.Message}"); }
                    await Task.Delay(500); // Be kind between reports
                }

                if (currentMonthEndDate >= targetMonthEndDate) break;
                currentMonthIterator = currentMonthIterator.AddMonths(1);
            }
            Console.WriteLine("\n--- Finished fetching all monthly reports ---");
     }

    // ==================================================================
    //          DATABASE HELPER METHODS (Inside Program class)
    // ==================================================================
    private static async Task<int> InsertJsonToSqlAsync(SqlConnection sqlConn, string rawTable, string jsonContent, Guid tenantId)
    {
        if (string.IsNullOrEmpty(jsonContent)) return 0;
        var insertSql = $"INSERT INTO dbo.{rawTable} (TenantGuid, JsonBody) VALUES (@TenantGuid, @JsonBody)";
        await using var cmd = new SqlCommand(insertSql, sqlConn);
        cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
        cmd.Parameters.AddWithValue("@JsonBody", jsonContent); // Ensure NVARCHAR(MAX)
        return await cmd.ExecuteNonQueryAsync();
    }

    private static async Task ProcessDataAsync(SqlConnection sqlConn, string procName)
    {
        Console.WriteLine($"🔨 Executing stored procedure {procName}...");
        try
        {
            await using var procCmd = new SqlCommand(procName, sqlConn) { CommandType = CommandType.StoredProcedure, CommandTimeout = 300 };
            await procCmd.ExecuteNonQueryAsync();
            Console.WriteLine($"👍 Finished executing {procName}.");
        }
        catch (SqlException procEx)
        {
            Console.WriteLine($"❌ Error executing stored procedure {procName}: {procEx.Message}");
            throw; // Rethrow
        }
    }

    private static async Task<DateTime?> GetLastSyncTimeAsync(SqlConnection sqlConn, Guid tenantId, string endpointName)
    {
        // Using exact table and column names provided by user
        var query = "SELECT MAX(LastUtc) FROM dbo.XeroSyncPoint WHERE TenantGuid = @TenantGuid AND Endpoint = @EndpointName";
        await using var cmd = new SqlCommand(query, sqlConn);
        cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
        cmd.Parameters.AddWithValue("@EndpointName", endpointName);

        var result = await cmd.ExecuteScalarAsync();
        if (result != null && result != DBNull.Value && result is DateTime dt)
        {
            return DateTime.SpecifyKind(dt, DateTimeKind.Utc); // Ensure UTC
        }
        // Handle DateTimeOffset if your column type is different
        // if (result is DateTimeOffset dto) return dto.UtcDateTime;
        return null;
    }

    private static async Task UpdateLastSyncTimeAsync(SqlConnection sqlConn, Guid tenantId, string endpointName, DateTime syncTimeUtc)
    {
        // Using exact table and column names provided by user
        var mergeSql = @"
            MERGE dbo.XeroSyncPoint AS target
            USING (SELECT @TenantGuid AS TenantGuid, @EndpointName AS Endpoint, @LastUtc AS LastUtc) AS source
            ON (target.TenantGuid = source.TenantGuid AND target.Endpoint = source.Endpoint)
            WHEN MATCHED THEN
                UPDATE SET LastUtc = source.LastUtc
            WHEN NOT MATCHED BY TARGET THEN
                INSERT (TenantGuid, Endpoint, LastUtc)
                VALUES (source.TenantGuid, source.Endpoint, source.LastUtc);";

        await using var cmd = new SqlCommand(mergeSql, sqlConn);
        cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
        cmd.Parameters.AddWithValue("@EndpointName", endpointName);
        cmd.Parameters.AddWithValue("@LastUtc", syncTimeUtc); // Assumes SQL column is DATETIME2

        try { await cmd.ExecuteNonQueryAsync(); }
        catch (SqlException mergeEx)
        {
            Console.WriteLine($"❌ Error updating sync timestamp for {endpointName} (Tenant: {tenantId}): {mergeEx.Message}");
            throw; // Rethrow
        }
    }

} // <-- *** END OF THE Program CLASS ***