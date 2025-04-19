using System;
using System.Collections.Generic; // Required for List
using System.Data; // Required for CommandType
using System.Threading.Tasks;
using MyNet8App.Services; // Assuming XeroReportFetcher is here
using Microsoft.Data.SqlClient; // Required for SqlConnection etc.
using System.Globalization;
using System.Net; // Required for HttpStatusCode

namespace MyNet8App.Services // Ensure namespace matches your structure
{
    public class MonthlyReportOrchestrator
    {
        public async Task RunAllMonthlyReports(string connectionString, string accessToken, string tenantIdStr, DateTime financialYearStartDate, DateTime targetMonthEndDate)
        {
            // --- 0. Validate Tenant ID ---
            if (!Guid.TryParse(tenantIdStr, out Guid tenantGuid) || tenantGuid == Guid.Empty)
            {
                Console.WriteLine("❌ Invalid Tenant ID provided to Report Orchestrator.");
                throw new ArgumentException("Invalid Tenant ID format.", nameof(tenantIdStr));
            }

            // --- 1. Instantiate the Fetcher ---
            var fetcher = new XeroReportFetcher(connectionString, accessToken, tenantGuid); // Pass Guid

            Console.WriteLine($"Starting monthly report generation from {financialYearStartDate:yyyy-MM-dd} to {targetMonthEndDate:yyyy-MM-dd} for Tenant: {tenantGuid}");

            // --- 2. Establish Database Connection ---
            // Open connection once for the whole process
            await using var sqlConn = new SqlConnection(connectionString);
            try
            {
                await sqlConn.OpenAsync();
                Console.WriteLine("✅ Report Orchestrator DB connection opened.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to open DB connection in Report Orchestrator: {ex.Message}");
                return; // Cannot proceed without DB connection
            }

            // --- 3. Loop Through Months ---
            DateTime currentMonthIterator = new DateTime(financialYearStartDate.Year, financialYearStartDate.Month, 1);

            while (currentMonthIterator <= targetMonthEndDate)
            {
                DateTime currentMonthEndDate = new DateTime(
                    currentMonthIterator.Year,
                    currentMonthIterator.Month,
                    DateTime.DaysInMonth(currentMonthIterator.Year, currentMonthIterator.Month)
                );
                if (currentMonthEndDate > targetMonthEndDate) currentMonthEndDate = targetMonthEndDate;

                Console.WriteLine($"\n--- Processing reports for month ending: {currentMonthEndDate:yyyy-MM-dd} ---");

                // --- 4. Define and Fetch Reports for the Current Month ---
                var reportsToFetch = new List<(string ReportName, string QueryParams)>
                {
                    // Query strings based on your previous version
                    ("ProfitAndLoss", $"fromDate={currentMonthIterator:yyyy-MM-dd}&toDate={currentMonthEndDate:yyyy-MM-dd}"),
                    ("BalanceSheet", $"fromDate={currentMonthEndDate:yyyy-MM-dd}&toDate={currentMonthEndDate:yyyy-MM-dd}"), // Adjusted based on original code logic
                    ("TrialBalance", $"fromDate={currentMonthEndDate:yyyy-MM-dd}&toDate={currentMonthEndDate:yyyy-MM-dd}")  // Adjusted based on original code logic
                    // Add other reports if needed
                };

                foreach (var reportInfo in reportsToFetch)
                {
                    bool success = false;
                    HttpStatusCode statusCode = HttpStatusCode.Unused;
                    DateTime? lastSyncTime = null;
                    string reportSyncName = $"Report_{reportInfo.ReportName}"; // Sync point name

                    try
                    {
                        // Get last sync time for this report
                        lastSyncTime = await GetLastSyncTimeAsync(sqlConn, tenantGuid, reportSyncName);

                        // Call FetchReportAsync with the lastSyncTime
                        (success, statusCode) = await fetcher.FetchReportAsync(reportInfo.ReportName, reportInfo.QueryParams, lastSyncTime);

                        // Update timestamp if fetch/process was successful (includes 304)
                        if (success)
                        {
                            await UpdateLastSyncTimeAsync(sqlConn, tenantGuid, reportSyncName, DateTime.UtcNow);
                            if (statusCode != HttpStatusCode.NotModified)
                            {
                                Console.WriteLine($"✅ Successfully processed {reportInfo.ReportName} and updated timestamp.");
                            }
                            else
                            {
                                Console.WriteLine($"✅ Timestamp updated for {reportInfo.ReportName} (No changes detected via 304).");
                            }
                        }
                        else
                        {
                             Console.WriteLine($"ℹ️ Report {reportInfo.ReportName} sync failed (Status: {statusCode}). Timestamp not updated.");
                        }
                    }
                    catch (Exception reportEx)
                    {
                         Console.WriteLine($"❌ Error fetching/processing report {reportInfo.ReportName} for {currentMonthEndDate:yyyy-MM-dd}: {reportEx.Message}");
                         // Log reportEx.ToString() for full details if needed
                    }
                    // Optional delay
                    await Task.Delay(500);
                } // End foreach report

                // --- 5. Prepare for Next Iteration ---
                if (currentMonthEndDate >= targetMonthEndDate) break;
                currentMonthIterator = currentMonthIterator.AddMonths(1);

            } // End while month

            Console.WriteLine("\n--- Finished fetching all monthly reports ---");
        } // End RunAllMonthlyReports


        // ==================================================================
        //          DATABASE HELPER METHODS
        // ==================================================================
        // NOTE: These are duplicated from Program.cs. Ideally, move to a shared static utility class.

        private static async Task<DateTime?> GetLastSyncTimeAsync(SqlConnection sqlConn, Guid tenantId, string endpointName)
        {
            var query = "SELECT MAX(LastUtc) FROM dbo.XeroSyncPoint WHERE TenantGuid = @TenantGuid AND Endpoint = @EndpointName";
            await using var cmd = new SqlCommand(query, sqlConn);
            cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
            cmd.Parameters.AddWithValue("@EndpointName", endpointName);
            var result = await cmd.ExecuteScalarAsync();
            if (result != null && result != DBNull.Value && result is DateTime dt) return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return null;
        }

        private static async Task UpdateLastSyncTimeAsync(SqlConnection sqlConn, Guid tenantId, string endpointName, DateTime syncTimeUtc)
        {
            var mergeSql = @"MERGE dbo.XeroSyncPoint AS target USING (SELECT @TenantGuid AS TenantGuid, @EndpointName AS Endpoint, @LastUtc AS LastUtc) AS source ON (target.TenantGuid = source.TenantGuid AND target.Endpoint = source.Endpoint) WHEN MATCHED THEN UPDATE SET LastUtc = source.LastUtc WHEN NOT MATCHED BY TARGET THEN INSERT (TenantGuid, Endpoint, LastUtc) VALUES (source.TenantGuid, source.Endpoint, source.LastUtc);";
            await using var cmd = new SqlCommand(mergeSql, sqlConn);
            cmd.Parameters.AddWithValue("@TenantGuid", tenantId);
            cmd.Parameters.AddWithValue("@EndpointName", endpointName);
            cmd.Parameters.AddWithValue("@LastUtc", syncTimeUtc);
            try { await cmd.ExecuteNonQueryAsync(); }
            catch (SqlException mergeEx) { Console.WriteLine($"❌ Error updating sync timestamp for {endpointName}: {mergeEx.Message}"); throw; }
        }

        // NOTE: The example usage Main method is removed as this is likely called from elsewhere.

    } // End class MonthlyReportOrchestrator
} // End namespace MyNet8App.Services