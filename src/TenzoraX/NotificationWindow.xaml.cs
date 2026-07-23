using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TenzoraX
{
    public partial class NotificationWindow : Window
    {
        private readonly double _duration;
        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TenzoraX", "notification.log");

        public NotificationWindow(string combo, string action, double duration)
        {
            try
            {
                InitializeComponent();
                Log("NotificationWindow created");
            }
            catch (Exception ex)
            {
                Log("NotificationWindow InitializeComponent ERROR: " + ex.Message);
            }

            TxtCombo.Text = combo;
            TxtAction.Text = action;
            _duration = duration;
        }

        public void BeginAnimation()
        {
            try
            {
                Log("BeginAnimation started, duration=" + _duration);

                double barWidth = ProgressBar.Width;
                Log("ProgressBar width=" + barWidth);

                var slideIn = new DoubleAnimation(-320, 0, TimeSpan.FromMilliseconds(350));
                slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
                BeginAnimation(OpacityProperty, fadeIn);

                var progressDelay = TimeSpan.FromMilliseconds(400);
                var progress = new DoubleAnimation(barWidth, 0, TimeSpan.FromSeconds(_duration));
                progress.BeginTime = progressDelay;
                ProgressBar.BeginAnimation(FrameworkElement.WidthProperty, progress);

                int totalMs = 400 + (int)(_duration * 1000);
                var totalVisible = TimeSpan.FromMilliseconds(totalMs);
                Log("Total visible time: " + totalMs + "ms");

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fadeOut.BeginTime = totalVisible;
                fadeOut.Completed += (s, e) =>
                {
                    try
                    {
                        BeginAnimation(OpacityProperty, null);
                        Log("Closing notification window");
                        Close();
                    }
                    catch (Exception ex) { Log("Close error: " + ex.Message); }
                };
                BeginAnimation(OpacityProperty, fadeOut);

                var slideOut = new DoubleAnimation(0, -320, TimeSpan.FromMilliseconds(300));
                slideOut.BeginTime = totalVisible;
                slideOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

                Log("BeginAnimation completed setup");
            }
            catch (Exception ex)
            {
                Log("BeginAnimation ERROR: " + ex.GetType().Name + ": " + ex.Message);
                try { Close(); } catch { }
            }
        }

        public void AnimateTop(double targetY)
        {
            try
            {
                var anim = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(300));
                anim.EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut };
                BeginAnimation(TopProperty, anim);
            }
            catch (Exception ex) { Log("AnimateTop error: " + ex.Message); }
        }

        private static void Log(string message)
        {
            try
            {
                string? dir = Path.GetDirectoryName(LogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                string entry = $"[{DateTime.Now:HH:mm:ss.fff}] {message}\n";
                File.AppendAllText(LogPath, entry);
            }
            catch { }
        }
    }
}
