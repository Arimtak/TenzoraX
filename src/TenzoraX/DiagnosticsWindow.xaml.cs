using System;
using System.Threading.Tasks;
using System.Windows;
using TenzoraX.Detection;

namespace TenzoraX
{
    public partial class DiagnosticsWindow : Window
    {
        private bool _loading;
        private bool _endpointsRunning;
        private readonly System.Windows.Threading.DispatcherTimer _latencyTimer;
        private readonly System.Windows.Threading.DispatcherTimer _endpointTimer;

        /// <summary>HidHide-Zeile, die zusammen mit dem Bericht neu ermittelt wird
        /// (ein CLI-Aufruf im Sekundentakt wäre unnötige Last).</summary>
        private InputEndpointSnapshot? _lastFullSnapshot;

        public DiagnosticsWindow()
        {
            InitializeComponent();
            ThemeHelper.StyleWindow(this);
            DataContext = LanguageManager.Instance;
            Title = LanguageManager.Instance["Diag_Title"];
            TxtHeading.Text = "🔍 " + LanguageManager.Instance["Diag_Title"];
            TxtEndpointsHeading.Text = "📡 " + LanguageManager.Instance["Diag_Endpoints"];

            // Live-Latenz-Anzeige: Eingabe→SendInput, Poll-Intervall, Dispatcher-Stau
            TxtLatency.Text = LatencyMonitor.FormatSummary();
            _latencyTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _latencyTimer.Tick += (s, e) => TxtLatency.Text = LatencyMonitor.FormatSummary();
            _latencyTimer.Start();

            // Live-Endpunkte: welche Geräte führt Windows gerade, und was zählt Steam daraus?
            _endpointTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _endpointTimer.Tick += async (s, e) => await RefreshEndpointsAsync();
            _endpointTimer.Start();

            _ = LoadReportAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _latencyTimer.Stop();
            _endpointTimer.Stop();
            _lastFullSnapshot = null;
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

            // HidHide-Zustand einmal mit dem Bericht holen (spart CLI-Aufrufe im Takt).
            await RefreshEndpointsAsync(withHidHideState: true);
        }

        /// <summary>
        /// Aktualisiert die Endpunkt-Anzeige. Läuft in einem Hintergrund-Thread, damit
        /// die Oberfläche während der Geräteabfrage nicht stockt.
        /// </summary>
        private async Task RefreshEndpointsAsync(bool withHidHideState = false)
        {
            if (_endpointsRunning) return;
            _endpointsRunning = true;
            try
            {
                var previous = _lastFullSnapshot;
                var result = await Task.Run(() =>
                {
                    var snapshot = InputEndpoints.Collect(includeHidHideState: withHidHideState);
                    if (!withHidHideState && previous != null)
                    {
                        // HidHide-Zustand aus der letzten Vollabfrage übernehmen
                        snapshot.HidHideInstalled = previous.HidHideInstalled;
                        snapshot.CloakActive = previous.CloakActive;
                        snapshot.HiddenDeviceCount = previous.HiddenDeviceCount;
                        snapshot.HidHideStateKnown = previous.HidHideStateKnown;
                    }
                    return (Text: InputEndpoints.Format(snapshot), Full: withHidHideState ? snapshot : null);
                });

                if (result.Full != null) _lastFullSnapshot = result.Full;
                TxtEndpoints.Text = result.Text;
            }
            catch (Exception ex)
            {
                TxtEndpoints.Text = "Endpunkt-Abfrage fehlgeschlagen: " + ex.Message;
            }
            finally
            {
                _endpointsRunning = false;
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
                // Endpunkte zuerst: sie sind die kurze Zusammenfassung vor dem langen Bericht.
                string text = string.IsNullOrWhiteSpace(TxtEndpoints.Text)
                    ? TxtReport.Text
                    : TxtEndpoints.Text + Environment.NewLine + TxtReport.Text;
                System.Windows.Clipboard.SetText(text);
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
