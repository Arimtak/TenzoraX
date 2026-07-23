using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace TenzoraX
{
    public partial class NotificationWindow : Window
    {
        private readonly double _duration;

        public NotificationWindow(string combo, string action, double duration)
        {
            try
            {
                InitializeComponent();
            }
            catch
            {
                // Fallback: create minimal window in code
            }
            TxtCombo.Text = combo;
            TxtAction.Text = action;
            _duration = duration;
        }

        public void BeginAnimation()
        {
            try
            {
                double barWidth = ProgressBar.Width;

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

                var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(250));
                fadeOut.BeginTime = totalVisible;
                fadeOut.Completed += (s, e) =>
                {
                    try
                    {
                        BeginAnimation(OpacityProperty, null);
                        Close();
                    }
                    catch { }
                };
                BeginAnimation(OpacityProperty, fadeOut);

                var slideOut = new DoubleAnimation(0, -320, TimeSpan.FromMilliseconds(300));
                slideOut.BeginTime = totalVisible;
                slideOut.EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn };
                SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
            }
            catch
            {
                // Animation failed – close immediately
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
