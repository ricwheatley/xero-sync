using System.Data.SqlClient;

namespace MyNet8App.Core;

public interface IReportOrchestrator
{
    Task RunAsync(RunMode mode,
                  SqlConnection sqlConn,
                  string connStr,
                  Guid tenantId,
                  string accessToken,
                  DateTime fyStart,
                  DateTime fyEnd,
                  CancellationToken ct);
}

public sealed class ReportOrchestrator : IReportOrchestrator
{
    private readonly ISupportDataRunner      _support;
    private readonly IFinancialReportRunner  _reports;

    public ReportOrchestrator(ISupportDataRunner support,
                              IFinancialReportRunner reports)
    {
        _support = support;
        _reports = reports;
    }

    public async Task RunAsync(RunMode mode,
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
            await _reports.RunAsync(sqlConn, connStr, tenantId, accessToken,
                                    fyStart, fyEnd, ct);
    }
}
