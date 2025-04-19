using System;
using Microsoft.Data.SqlClient;
using System.Threading;
using System.Threading.Tasks;
using XeroSync.Worker.Core;

namespace XeroSync.Worker
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("Usage: dotnet run --project XeroSync.Worker -- <support|reports|both> [yyyy-MM-dd yyyy-MM-dd]");
                return 1;
            }

            if (!Enum.TryParse<RunMode>(args[0], true, out var mode))
            {
                Console.WriteLine($"Invalid RunMode '{args[0]}'. Use support, reports or both.");
                return 1;
            }

            // Optional financial‑year date range
            DateTime fyStart = DateTime.MinValue, fyEnd = DateTime.MaxValue;
            if (args.Length >= 3)
            {
                if (!DateTime.TryParse(args[1], out fyStart) || !DateTime.TryParse(args[2], out fyEnd))
                {
                    Console.WriteLine("Invalid date format. Use yyyy-MM-dd.");
                    return 1;
                }
            }

            // Token and tenant set‑up (in ProgramHelpers.cs)
            var accessToken = await TokenHelper.AcquireTokenAsync();
            var tenantId    = await TenantHelper.DiscoverTenantAsync(accessToken);

            // Open SQL connection
            var connStr = Configuration.GetConnectionString("XeroSync");
            await using var sqlConn = new SqlConnection(connStr);
            await sqlConn.OpenAsync();

            // Orchestrate SupportData and Reports
            var orchestrator = new ReportOrchestrator(
                new SupportDataRunner(),
                new FinancialReportRunner()
            );

            await orchestrator.RunAsync(
                mode,
                sqlConn,
                connStr,
                tenantId,
                accessToken,
                fyStart,
                fyEnd,
                CancellationToken.None
            );

            return 0;
        }
    }
}
