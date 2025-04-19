namespace MyNet8App.Core;

public interface IReportService
{
    Task RunAsync(DateOnly start, DateOnly end, RunMode mode, CancellationToken ct);
}
