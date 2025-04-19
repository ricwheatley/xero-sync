// File: XeroSync.Worker/Core/ReportOrchestrator.cs
using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;

namespace XeroSync.Worker.Core
{
    public sealed class ReportOrchestrator : IReportOrchestrator
    {
        private readonly ISupportDataRunner _support;
        private readonly IFinancialReportRunner _reports;

        public ReportOrchestrator(ISupportDataRunner support, IFinancialReportRunner reports)
        {
            _support = support ?? throw new ArgumentNullException(nameof(support));
            _reports = reports ?? throw new ArgumentNullException(nameof(reports));
        }

        public async Task RunAsync(
            RunMode mode,
            SqlConnection sqlConn,
            string connStr,
            Guid tenantId,
            string accessToken,
            DateTime fyStart,
            DateTime fyEnd,
            CancellationToken ct)
        {
            if (mode is RunMode.SupportData or RunMode.Both)
                await _support.RunAsync(sqlConn, tenantId, accessToken, ct);

            if (mode is RunMode.Reports or RunMode.Both)
                await _reports.RunAsync(sqlConn, connStr, tenantId, accessToken, fyStart, fyEnd, ct);
        }
    }
}
