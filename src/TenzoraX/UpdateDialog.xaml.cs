using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TenzoraX
{
    public partial class UpdateDialog : Window
    {
        private readonly UpdateInfo _info;
        private bool _updateNow = false;

        public string? DownloadedExePath { get; private set; }

        public UpdateDialog(UpdateInfo info)
        {
            InitializeComponent();
            _info = info;
            TxtUpdateMsg.Text = "Eine neue Version von TenzoraX ist verfügbar.";
            TxtInstalled.Text = $"v{AppVersion.Current}";
            TxtNewVersion.Text = $"v{info.LatestVersion}";
        }

        public static UpdateDialog? ShowUpdate(Window owner, UpdateInfo info)
        {
            var dlg = new UpdateDialog(info) { Owner = owner };
            dlg.ShowDialog();
            return dlg._updateNow ? dlg : null;
        }

        private void BtnUpdate_Click(object sender, RoutedEventArgs e)
        {
            BtnUpdateNow.IsEnabled = false;
            BtnUpdateLater.IsEnabled = false;
            BtnUpdateNow.Content = "Wird geladen...";
            PanelProgress.Visibility = Visibility.Visible;

            TxtProgressStatus.Text = "Update wird heruntergeladen...";

            var progress = new Progress<int>(pct =>
            {
                ProgressBar.Value = pct;
                TxtProgressPct.Text = $"{pct} %";
            });

            var worker = new BackgroundWorker();
            worker.DoWork += (s, args) =>
            {
                args.Result = UpdateManager.DownloadUpdate(_info.DownloadUrl, progress).GetAwaiter().GetResult();
            };
            worker.RunWorkerCompleted += (s, args) =>
            {
                if (args.Result is string exePath && File.Exists(exePath))
                {
                    TxtProgressStatus.Text = "Update wird installiert...";
                    ProgressBar.Value = 100;
                    TxtProgressPct.Text = "100 %";

                    DownloadedExePath = exePath;
                    _updateNow = true;
                    Close();
                }
                else
                {
                    TxtProgressStatus.Text = "Download fehlgeschlagen. Bitte erneut versuchen.";
                    BtnUpdateNow.Content = "Wiederholen";
                    BtnUpdateNow.IsEnabled = true;
                    BtnUpdateLater.IsEnabled = true;
                    PanelProgress.Visibility = Visibility.Collapsed;
                }
            };
            worker.RunWorkerAsync();
        }

        private void BtnLater_Click(object sender, RoutedEventArgs e)
        {
            _updateNow = false;
            Close();
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                DragMove();
        }
    }
}
