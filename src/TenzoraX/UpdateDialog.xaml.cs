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
            var lang = LanguageManager.Instance;
            string loading = lang.CurrentLang == "de" ? "Lade..." : "Loading...";
            string retry = lang.CurrentLang == "de" ? "Wiederholen" : "Retry";
            BtnUpdateNow.Content = loading;

            var worker = new BackgroundWorker();
            worker.DoWork += (s, args) =>
            {
                args.Result = UpdateManager.DownloadUpdate(_info.DownloadUrl).GetAwaiter().GetResult();
            };
            worker.RunWorkerCompleted += (s, args) =>
            {
                if (args.Result is string exePath && File.Exists(exePath))
                {
                    DownloadedExePath = exePath;
                    _updateNow = true;
                    Close();
                }
                else
                {
                    BtnUpdateNow.Content = retry;
                    BtnUpdateNow.IsEnabled = true;
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
