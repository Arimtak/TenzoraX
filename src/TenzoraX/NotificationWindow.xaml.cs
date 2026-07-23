using System;
using System.Threading.Tasks;
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

        public NotificationWindow(string combo, string action, double duration, double width)
        {
            try
            {
                InitializeComponent();
                Width = width;
                ProgressBar.Width = width - 40;
            }
            catch (Exception ex)
            {
                App.LogApp("NotificationWindow init ERROR: " + ex.Message);
            }

            TxtCombo.Text = combo;
            TxtAction.Text = action;
            _duration = duration;

            MouseDown += OnMouseDown;
            MouseMove += OnMouseMove;
            MouseUp += OnMouseUp;
            Closed += OnClosed;
        }

        private void OnClosed(object? sender, EventArgs e)
        {
            // Stop all animations
            try
            {
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
                BeginAnimation(OpacityProperty, null);
                ProgressBar.BeginAnimation(FrameworkElement.WidthProperty, null);
            }
            catch { }
            // Unregister events
            MouseDown -= OnMouseDown;
            MouseMove -= OnMouseMove;
            MouseUp -= OnMouseUp;
            Closed -= OnClosed;
        }

        private void OnMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                _isDragging = true;
                _dragStart = new System.Windows.Point(Left, Top);
                CaptureMouse();
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
            }
        }

        public async void BeginAnimation()
        {
            try
            {
                double barWidth = ProgressBar.Width;

                // ===== PHASE 1: Slide In (0.8s) =====
                var slideIn = new DoubleAnimation(-360, 0, TimeSpan.FromMilliseconds(800));
                slideIn.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

                var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
                BeginAnimation(OpacityProperty, fadeIn);

                await Task.Delay(800).ConfigureAwait(true);
                if (!IsLoaded) return;

                // ===== PHASE 2: Visible / Wait (_duration seconds) =====
                var progress = new DoubleAnimation(barWidth, 0, TimeSpan.FromSeconds(_duration));
                ProgressBar.BeginAnimation(FrameworkElement.WidthProperty, progress);

                await Task.Delay((int)(_duration * 1000)).ConfigureAwait(true);
                if (!IsLoaded) return;

                // ===== PHASE 3: Slide Out (0.9s) =====
                var slideOut = new DoubleAnimation(0, -360, TimeSpan.FromMilliseconds(900));
                slideOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(900));
                fadeOut.Completed += (s, args) =>
                {
                    try
                    {
                        BeginAnimation(OpacityProperty, null);
                        Close();
                    }
                    catch { }
                };
                BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                App.LogApp("Notification Animation-Fehler: " + ex.Message);
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
            catch { }
        }
    }
}
