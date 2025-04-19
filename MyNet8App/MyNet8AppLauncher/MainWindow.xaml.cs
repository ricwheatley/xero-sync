using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace MyNet8AppLauncher;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void RunButton_Click(object sender, RoutedEventArgs e)
    {
        // Basic validation
        if (StartDatePicker.SelectedDate is null || EndDatePicker.SelectedDate is null)
        {
            MessageBox.Show("Please select both start and end dates.", "Validation",
                            MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Build command‑line args
        string start = ((DateTime)StartDatePicker.SelectedDate).ToString("yyyy-MM-dd");
        string end   = ((DateTime)EndDatePicker.SelectedDate).ToString("yyyy-MM-dd");
        string mode  = ((ComboBoxItem)ModeComboBox.SelectedItem)?.Tag?.ToString() ?? "both";

        string arguments = $"--start {start} --end {end} --mode {mode}";

        // Assume MyNet8App.exe sits next to this launcher
        string exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "MyNet8App.exe");
        if (!File.Exists(exePath))
        {
            MessageBox.Show($"Cannot find {exePath}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        RunButton.IsEnabled = false;
        LogTextBox.Clear();

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = new Process { StartInfo = psi };
            proc.OutputDataReceived += (_, e2) => AppendLine(e2.Data);
            proc.ErrorDataReceived  += (_, e2) => AppendLine(e2.Data);

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            await proc.WaitForExitAsync();

            AppendLine($"--- exited with code {proc.ExitCode} ---");
        }
        catch (Exception ex)
        {
            AppendLine("ERROR: " + ex.Message);
        }
        finally
        {
            RunButton.IsEnabled = true;
        }
    }

    private void AppendLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;
        Dispatcher.Invoke(() =>
        {
            LogTextBox.AppendText(line + Environment.NewLine);
            LogTextBox.ScrollToEnd();
        });
    }
}
