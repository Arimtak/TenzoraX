using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TenzoraX
{
    public partial class NotificationWindow : Window
    {
        private readonly double _duration;
        private bool _isDragging;
        private System.Windows.Point _dragStart;
        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TenzoraX", "notification.log");

        public NotificationWindow(string combo, string action, double duration, double width)
        {
            try
            {
                InitializeComponent();
                Width = width;
                ProgressBar.Width = width - 40;
                Log("NotificationWindow created, width=" + width);
            }
            catch (Exception ex)
            {
                Log("NotificationWindow init ERROR: " + ex.Message);
            }

            TxtCombo.Text = combo;
            TxtAction.Text = action;
            _duration = duration;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
        }

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStart = new System.Windows.Point(Left, Top);
                CaptureMouse();
                Log("Drag started");
            }
        }

        private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_isDragging)
            {
                var pos = PointToScreen(e.GetPosition(this));
                Left = pos.X;
                Top = pos.Y;
            }
        }

        private void OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                Log($"Drag ended: X={Left} Y={Top}");
            }
        }

        public void BeginAnimation()
        {
            try
            {
                Log("BeginAnimation started, duration=" + _duration);

                double barWidth = ProgressBar.Width;

                var slideIn = new DoubleAnimation(-360, 0, TimeSpan.FromMilliseconds(600));
                slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250));
                BeginAnimation(OpacityProperty, fadeIn);

                int slideInMs = 650;
                var progressDelay = TimeSpan.FromMilliseconds(slideInMs);
                var progress = new DoubleAnimation(barWidth, 0, TimeSpan.FromSeconds(_duration));
                progress.BeginTime = progressDelay;
                ProgressBar.BeginAnimation(FrameworkElement.WidthProperty, progress);

                int totalMs = slideInMs + (int)(_duration * 1000);
                var totalVisible = TimeSpan.FromMilliseconds(totalMs);

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(800));
                fadeOut.BeginTime = totalVisible;
                fadeOut.Completed += (s, e) =>
                {
                    try
                    {
                        BeginAnimation(OpacityProperty, null);
                        Log("Closing notification");
                        Close();
                    }
                    catch (Exception ex) { Log("Close error: " + ex.Message); }
                };
                BeginAnimation(OpacityProperty, fadeOut);

                var slideOut = new DoubleAnimation(0, -360, TimeSpan.FromMilliseconds(800));
                slideOut.BeginTime = totalVisible;
                slideOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

                Log("BeginAnimation setup done");
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
