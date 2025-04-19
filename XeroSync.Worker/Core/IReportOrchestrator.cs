using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace XeroSync.Worker.Core
{
    public interface IReportOrchestrator
    {
        Task RunAsync(
            RunMode mode,
            SqlConnection sqlConn,
            string connStr,
            Guid tenantId,
            string accessToken,
            DateTime fyStart,
            DateTime fyEnd,
            CancellationToken ct);
    }
}
