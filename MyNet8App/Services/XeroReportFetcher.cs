using System;
using System.Data;
using System.Net; // Required for HttpStatusCode
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using RestSharp;

namespace MyNet8App.Services // Match the namespace used in Program.cs
{
    public class XeroReportFetcher
    {
        private readonly string _connectionString;
        private readonly string _accessToken;
        private readonly Guid _tenantId;
        private readonly RestClient _restClient;
        private const string XeroApiBaseUrl = "https://api.xero.com/api.xro/2.0/";

        public XeroReportFetcher(string connectionString, string accessToken, Guid tenantId)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
            if (tenantId == Guid.Empty) throw new ArgumentException("Tenant ID cannot be empty.", nameof(tenantId));
            _tenantId = tenantId;
            _restClient = new RestClient(XeroApiBaseUrl);
        }

        /// <summary>
        /// Fetches a Xero Report. Handles If-Modified-Since. Saves raw JSON.
        /// </summary>
        /// <returns>A tuple: (bool Success, HttpStatusCode StatusCode)</returns>
        public async Task<(bool Success, HttpStatusCode StatusCode)> FetchReportAsync(string reportName, string queryString, DateTime? lastSyncTime)
        {
            Console.WriteLine($"\n📬 Fetching Report: {reportName}...");
             if (lastSyncTime.HasValue) Console.WriteLine($"    (Incremental sync since {lastSyncTime.Value:yyyy-MM-dd HH:mm:ss} UTC)");

            var resourceUri = $"Reports/{reportName}";
            if (!string.IsNullOrWhiteSpace(queryString)) resourceUri += $"?{queryString.TrimStart('?')}";

            var request = new RestRequest(resourceUri, Method.Get);
            request.AddHeader("Authorization", $"Bearer {_accessToken}");
            request.AddHeader("xero-tenant-id", _tenantId.ToString());
            request.AddHeader("Accept", "application/json");

            if (lastSyncTime.HasValue)
            {
                request.AddHeader("If-Modified-Since", lastSyncTime.Value.ToUniversalTime().ToString("R"));
            }

            Console.WriteLine($"    -> Calling {resourceUri} {(lastSyncTime.HasValue ? "| If-Modified-Since Used" : "")}");

            RestResponse response;
            try { response = await _restClient.ExecuteAsync(request); }
            catch (Exception clientEx)
            {
                 Console.WriteLine($"❌ Exception during API call for report {reportName}: {clientEx.Message}");
                 return (false, HttpStatusCode.ServiceUnavailable); // Indicate failure
            }

            // --- Handle Response ---
            if (lastSyncTime.HasValue && response.StatusCode == HttpStatusCode.NotModified)
            {
                Console.WriteLine($"✅ Report {reportName}: Not Modified (304).");
                return (true, HttpStatusCode.NotModified); // Indicate success (no change)
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                 Console.WriteLine($"⚠️ Report {reportName}: Rate limit hit (429).");
                 return (false, HttpStatusCode.TooManyRequests); // Indicate failure
            }

            // Check for general success AND non-empty content for reports
            if (!response.IsSuccessful || string.IsNullOrEmpty(response.Content))
            {
                Console.WriteLine($"❌ Report {reportName} call failed or empty: {response.StatusCode} – {response.ErrorMessage} {(string.IsNullOrEmpty(response.Content) ? "[Empty Content]" : "")}");
                return (false, response.StatusCode); // Indicate failure
            }

            // --- Process successful report data ---
             Console.WriteLine($"    <- Received Report {reportName} ({response.Content.Length:N0} chars)");

            var rawTable = $"XeroRaw_{reportName}";
            var procName = $"Process{reportName}Raw";
            bool processSuccess = false;

            try
            {
                await using var sqlConn = new SqlConnection(_connectionString);
                await sqlConn.OpenAsync();

                var rowsInserted = await InsertJsonToSqlAsync(sqlConn, rawTable, response.Content, _tenantId);
                Console.WriteLine($"💾 Inserted {rowsInserted} row(s) into {rawTable}.");

                if (rowsInserted > 0)
                {
                     await ProcessDataAsync(sqlConn, procName);
                     processSuccess = true; // Processing finished
                }
                 else
                 {
                     Console.WriteLine($"ℹ️ No rows inserted for report {reportName}, skipping processing SP.");
                     processSuccess = true; // Still consider the fetch part successful
                 }
            }
            catch (Exception dbEx)
            {
                Console.WriteLine($"❌ Error saving/processing report {reportName} data: {dbEx.Message}");
                processSuccess = false;
            }

            // Return overall success and the original OK status code
            return (processSuccess, response.StatusCode);
        }


        // ==================================================================
        //          DATABASE HELPER METHODS (Copied for Scope)
        // ==================================================================
        private static async Task<int> InsertJsonToSqlAsync(SqlConnection sqlConn, string rawTable, string jsonContent, Guid tenantId)
        {
            if (string.IsNullOrEmpty(jsonContent)) return 0;
            var insertSql = $"INSERT INTO dbo.{rawTable} (TenantGuid, JsonBody) VALUES (@TenantGuid, @JsonBody)";
            await using var cmd = new SqlCommand(insertSql, sqlConn);
            cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
            cmd.Parameters.AddWithValue("@JsonBody", jsonContent);
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
    } // End class
} // End namespace