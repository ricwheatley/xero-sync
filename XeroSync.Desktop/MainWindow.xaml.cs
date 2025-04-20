using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using XeroSync.Desktop.Services;
using XeroSync.Worker;
using XeroSync.Worker.Core;

namespace XeroSync.Desktop
{
    public partial class MainWindow : Window
    {
        private CancellationTokenSource? _cts;   // cancels the worker if user closes mid‑run

        public MainWindow()
        {
            InitializeComponent();

            // Console → log
            Console.SetOut(new TextBoxWriter(AppendLog));

            Loaded += Window_Loaded;
        }

        // ---------- asynchronous UI initialisation ----------
        private async void Window_Loaded(object? sender, RoutedEventArgs e)
        {
            try
            {
                AppendLog("🔑 Getting token…");
                var token  = await TokenHelper.AcquireTokenAsync();

                AppendLog("🔍 Discovering tenant…");
                var tenant = await TenantHelper.DiscoverTenantAsync(token);

                TenantBox.ItemsSource  = new[] { tenant.ToString() };
                TenantBox.SelectedItem = tenant.ToString();

                StartPicker.SelectedDate = DateTime.Today.AddMonths(-1);
                EndPicker.SelectedDate   = DateTime.Today;
                AppendLog("✅ Ready");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Startup failed: {ex.Message}");
            }
        }

        // ---------- RUN ----------
        private async void RunClicked(object sender, RoutedEventArgs e)
        {
            RunButton.IsEnabled = false;          // prevent double‑click
            _cts = new CancellationTokenSource(); // allow abort if window closed

            try
            {
                var mode = (string)((ComboBoxItem)ModeBox.SelectedItem!).Content;

                var settings = new UiSettings(
                    Guid.Parse((string)TenantBox.SelectedItem!),
                    mode,
                    StartPicker.SelectedDate!.Value,
                    EndPicker.SelectedDate!.Value);
                UiSettingsStore.Save(settings);

                AppendLog("🚀 Sync started…");

                var token = await TokenHelper.AcquireTokenAsync();
                using var sql = new Microsoft.Data.SqlClient.SqlConnection(
                    Configuration.GetConnectionString(""));
                await sql.OpenAsync(_cts.Token);

                var orchestrator = new ReportOrchestrator(
                    new SupportDataRunner(),
                    new FinancialReportRunner());

                await orchestrator.RunAsync(
                    Enum.Parse<RunMode>(settings.RunMode),
                    sql,
                    sql.ConnectionString,
                    settings.TenantGuid,
                    token,
                    settings.FyStart,
                    settings.FyEnd,
                    _cts.Token);

                AppendLog("✅ Completed");
            }
            catch (OperationCanceledException)
            {
                AppendLog("⏹️ Aborted by user");
            }
            catch (Exception ex)
            {
                AppendLog($"❌ Error: {ex.Message}");
            }
            finally
            {
                RunButton.IsEnabled = true;
                _cts = null;
            }
        }

        // ---------- CLOSE ----------
        private void CloseClicked(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();  // if a sync is running, cancel it gracefully
            Close();         // then close the window (same as red X)
        }

        // ---------- log helper ----------
        private void AppendLog(string line)
        {
            LogBox.AppendText(line + Environment.NewLine);
            LogBox.ScrollToEnd();
        }
    }

    // Redirects Console.WriteLine to the UI log
    public sealed class TextBoxWriter : TextWriter
    {
        private readonly Action<string> _write;
        public TextBoxWriter(Action<string> write) => _write = write;
        public override Encoding Encoding => Encoding.UTF8;
        public override void WriteLine(string? value) => _write(value ?? "");
        public override void Write(char value) { /* ignore */ }
    }
}
