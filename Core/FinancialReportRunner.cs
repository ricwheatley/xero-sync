using System.Data.SqlClient;

namespace MyNet8App.Core;

public interface IFinancialReportRunner
{
    Task RunAsync(SqlConnection sqlConn,
                  string connStr,
                  Guid tenantId,
                  string accessToken,
                  DateTime financialYearStart,
                  DateTime targetMonthEnd,
                  CancellationToken ct);
}

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
        Console.WriteLine("\n🚀 Starting Report Fetching...");

        // 🡆 COPY the while‑loop from FetchAndProcessReportsAsync
        //    into this method, unchanged, then delete the old method.

        // Provide cancellation support by sprinkling `ct.ThrowIfCancellationRequested();`
        // in the long loops if you like.
    }

    // ─────────────────────────────────────────────────────────
    //  Private helpers – paste the per‑report work (and any
    //  DB helpers you want to keep local) down here.
    // ─────────────────────────────────────────────────────────
}
