using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace XeroSync.Worker.Core
{
    public interface ISupportDataRunner
    {
        Task RunAsync(
            SqlConnection sqlConn,
            Guid tenantId,
            string accessToken,
            CancellationToken ct);
    }
}
