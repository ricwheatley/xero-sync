using MyNet8App.Services;          // whatever namespace your existing classes are in

namespace MyNet8App.Core;

public sealed class ReportService : IReportService
{
    private readonly XeroReportFetcher    _reports;
    private readonly MonthlyReportOrchestrator _support;

    public ReportService(XeroReportFetcher reports,
                         MonthlyReportOrchestrator support)
    {
        _reports = reports;
        _support = support;
    }

    public async Task RunAsync(DateOnly start, DateOnly end, RunMode mode, CancellationToken ct)
    {
        switch (mode)
        {
            case RunMode.Reports:
                await _reports.RunAsync(start, end, ct);
                break;

            case RunMode.SupportData:
                await _support.RunAsync(start, end, ct);
                break;

            case RunMode.Both:
                await _reports.RunAsync(start, end, ct);
                await _support.RunAsync(start, end, ct);
                break;
        }
    }
}
