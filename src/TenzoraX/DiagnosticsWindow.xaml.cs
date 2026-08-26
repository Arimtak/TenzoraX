using System;
using System.Threading.Tasks;
using System.Windows;

namespace TenzoraX
{
    public partial class DiagnosticsWindow : Window
    {
        private bool _loading;
        private readonly System.Windows.Threading.DispatcherTimer _latencyTimer;

        public DiagnosticsWindow()
        {
            InitializeComponent();
            ThemeHelper.StyleWindow(this);
            DataContext = LanguageManager.Instance;
            Title = LanguageManager.Instance["Diag_Title"];
            TxtHeading.Text = "🔍 " + LanguageManager.Instance["Diag_Title"];

            // Live-Latenz-Anzeige: Eingabe→SendInput, Poll-Intervall, Dispatcher-Stau
            TxtLatency.Text = LatencyMonitor.FormatSummary();
            _latencyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _latencyTimer.Tick += (s, e) => TxtLatency.Text = LatencyMonitor.FormatSummary();
            _latencyTimer.Start();

            _ = LoadReportAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _latencyTimer.Stop();
            base.OnClosed(e);
        }

        private async Task LoadReportAsync()
        {
            if (_loading) return;
            _loading = true;
            try
            {
                SetBusy(true);
                string report = await Task.Run(() => ControllerManager.Instance.BuildDiagnosticsReport());
                TxtReport.Text = report;
                TxtReport.ScrollToHome();
            }
            catch (Exception ex)
            {
                TxtReport.Text = "Diagnose fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                SetBusy(false);
                _loading = false;
            }
        }

        private void SetBusy(bool busy)
        {
            BtnRefresh.IsEnabled = !busy;
            BtnCopy.IsEnabled = !busy;
            Cursor = busy ? System.Windows.Input.Cursors.Wait : System.Windows.Input.Cursors.Arrow;
        }

        private async void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            await LoadReportAsync();
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Windows.Clipboard.SetText(TxtReport.Text);
                BtnCopy.Content = "✓ Kopiert";
                var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                t.Tick += (s, _) => { BtnCopy.Content = "📋 Kopieren"; t.Stop(); };
                t.Start();
            }
            catch { }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
