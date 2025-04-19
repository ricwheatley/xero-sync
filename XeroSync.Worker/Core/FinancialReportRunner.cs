using System;
using System.Collections.Generic;
using Microsoft.Data.SqlClient;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using XeroSync.Worker.Services;
namespace XeroSync.Worker.Core;

public sealed class FinancialReportRunner : IFinancialReportRunner
{
    public async Task RunAsync(SqlConnection sqlConn,
                                string connStr,
                                Guid tenantId,
                                string accessToken,
                                DateTime financialYearStart,
                                DateTime targetMonthEnd,
                                CancellationToken ct)
    {
        var fetcher = new Services.XeroReportFetcher(connStr, accessToken, tenantId);  // adjust if moved

        DateTime current = new(financialYearStart.Year, financialYearStart.Month, 1);

        while (current <= targetMonthEnd)
        {
            DateTime monthEnd = new DateTime(current.Year, current.Month,
                                DateTime.DaysInMonth(current.Year, current.Month));
            if (monthEnd > targetMonthEnd)
                monthEnd = targetMonthEnd;

            Console.WriteLine($"\n--- Processing reports for month ending: {monthEnd:yyyy-MM-dd} ---");

            var reportsToFetch = new List<(string Name, string Params)>
            {
                ("ProfitAndLoss", $"fromDate={current:yyyy-MM-dd}&toDate={monthEnd:yyyy-MM-dd}"),
                ("BalanceSheet",  $"date={monthEnd:yyyy-MM-dd}"),
                ("TrialBalance",  $"date={monthEnd:yyyy-MM-dd}")
            };

            foreach (var (reportName, queryParams) in reportsToFetch)
            {
                string syncKey = $"Report_{reportName}";
                DateTime? lastSync = await GetLastSyncTimeAsync(sqlConn, tenantId, syncKey);
                bool success = false;
                HttpStatusCode status = HttpStatusCode.Unused;

                try
                {
                    (success, status) = await fetcher.FetchReportAsync(reportName, queryParams, lastSync);

                    if (success)
                    {
                        await UpdateLastSyncTimeAsync(sqlConn, tenantId, syncKey, DateTime.UtcNow);
                        if (status != HttpStatusCode.NotModified)
                            Console.WriteLine($"✅ Processed {reportName}");
                        else
                            Console.WriteLine($"✅ No change for {reportName}");
                    }
                    else
                    {
                        Console.WriteLine($"⚠️ {reportName} failed. Timestamp not updated.");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"❌ {reportName} failed: {ex.Message}");
                }

                await Task.Delay(500, ct);
            }

            if (monthEnd >= targetMonthEnd) break;
            current = current.AddMonths(1);
        }

        Console.WriteLine("\n✅ Finished fetching all monthly reports.");
    }

    private async Task<DateTime?> GetLastSyncTimeAsync(SqlConnection sqlConn, Guid tenantId, string endpointName)
    {
        const string query = "SELECT MAX(LastUtc) FROM dbo.XeroSyncPoint WHERE TenantGuid = @Tenant AND Endpoint = @EndpointName";
        await using var cmd = new SqlCommand(query, sqlConn);
        cmd.Parameters.AddWithValue("@Tenant", tenantId);
        cmd.Parameters.AddWithValue("@EndpointName", endpointName);
        var result = await cmd.ExecuteScalarAsync();
        return result is DateTime dt ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : null;
    }

    private async Task UpdateLastSyncTimeAsync(SqlConnection sqlConn, Guid tenantId, string endpointName, DateTime timestamp)
    {
        const string sql = @"
            MERGE dbo.XeroSyncPoint AS target
            USING (SELECT @Tenant AS TenantGuid, @EndpointName AS Endpoint, @LastUtc AS LastUtc) AS source
            ON (target.TenantGuid = source.TenantGuid AND target.Endpoint = source.Endpoint)
            WHEN MATCHED THEN UPDATE SET LastUtc = source.LastUtc
            WHEN NOT MATCHED THEN INSERT (TenantGuid, Endpoint, LastUtc)
            VALUES (source.TenantGuid, source.Endpoint, source.LastUtc);";

        await using var cmd = new SqlCommand(sql, sqlConn);
        cmd.Parameters.AddWithValue("@Tenant", tenantId);
        cmd.Parameters.AddWithValue("@EndpointName", endpointName);
        cmd.Parameters.AddWithValue("@LastUtc", timestamp);
        await cmd.ExecuteNonQueryAsync();
    }
}
