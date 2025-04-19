using System.Data.SqlClient;

namespace MyNet8App.Core;

public interface ISupportDataRunner
{
    Task RunAsync(SqlConnection sqlConn,
                  Guid tenantId,
                  string accessToken,
                  CancellationToken ct);
}

public sealed class SupportDataRunner : ISupportDataRunner
{
    private readonly List<string> _endpoints = new()
    {
        "Invoices", "Contacts", "Accounts", "BankTransactions",
        "TrackingCategories", "CreditNotes", "Payments", "PurchaseOrders"
    };

    public async Task RunAsync(SqlConnection sqlConn,
                               Guid tenantId,
                               string accessToken,
                               CancellationToken ct)
    {
        Console.WriteLine("\n🚀 Starting Standard Endpoint Sync...");

        foreach (var endpoint in _endpoints)
        {
            // 🡆 COPY the entire loop body from your existing code
            //    (IngestDataAsync, UpdateLastSyncTimeAsync, etc.)
            //    into a private method on this class, e.g.
            //
            // await SyncEndpointAsync(sqlConn, endpoint, tenantId, accessToken, ct);
        }
    }

    // ─────────────────────────────────────────────────────────
    //  Private helpers – paste your current IngestDataAsync,
    //  FetchPaginatedDataFromXeroAsync, etc. in here.
    // ─────────────────────────────────────────────────────────
}
