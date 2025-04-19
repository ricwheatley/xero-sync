using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace XeroSync.Worker.Core
{
    public interface IFinancialReportRunner
    {
        Task RunAsync(
            SqlConnection sqlConn,
            string connStr,
            Guid tenantId,
            string accessToken,
            DateTime fyStart,
            DateTime fyEnd,
            CancellationToken ct);
    }
}
