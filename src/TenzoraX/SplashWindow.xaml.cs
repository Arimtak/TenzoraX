using System;
using System.Windows;
using System.Windows.Threading;

namespace TenzoraX
{
    public partial class SplashWindow : Window
    {
        private static SplashWindow? _instance;

        public SplashWindow()
        {
            InitializeComponent();
            TxtVersion.Text = "v" + AppVersion.Current;
            _instance = this;
        }

        public static void ShowSplash()
        {
            if (_instance != null) return;
            _instance = new SplashWindow();
            _instance.Show();
            _instance.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
        }

        public static void SetStatus(string text, double progress)
        {
            _instance?.Dispatcher.Invoke(() =>
            {
                _instance.TxtStatus.Text = text;
                _instance.ProgressBar.Value = progress;
            });
        }

        public static void CloseSplash()
        {
            _instance?.Dispatcher.Invoke(() =>
            {
                _instance.Close();
                _instance = null;
            });
        }
    }
}
