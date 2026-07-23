using System;
using System.Collections.Generic;
using System.Linq;

namespace TenzoraX
{
    public static class NotificationManager
    {
        private static readonly List<NotificationWindow> _activeNotifications = new();
        private static int _monitorIndex = -1;
        private static bool _enabled = true;
        private static double _duration = 1.5;

        public static bool Enabled
        {
            get => _enabled;
            set => _enabled = value;
        }

        public static double Duration
        {
            get => _duration;
            set => _duration = Math.Clamp(value, 0.5, 10.0);
        }

        public static int MonitorIndex
        {
            get => _monitorIndex;
            set => _monitorIndex = value;
        }

        public static void Show(string combo, string action)
        {
            if (!_enabled) return;
            try
            {
                var screen = GetTargetScreen();
                if (screen == null) return;

                var notification = new NotificationWindow(combo, action, _duration);
                notification.Show();
                notification.Opacity = 0;

                double baseLeft = screen.WorkingArea.Left + 16;
                double centerY = screen.WorkingArea.Top + screen.WorkingArea.Height / 2;
                double height = Math.Max(notification.ActualHeight, 60);

                var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
                double totalStackH = visible.Sum(n => Math.Max(n.ActualHeight, 60) + 8);
                notification.Top = Math.Max(screen.WorkingArea.Top + 8, centerY - totalStackH - height / 2);
                notification.Left = baseLeft;

                notification.Closed += (s, e) =>
                {
                    _activeNotifications.Remove(notification);
                    try { RepositionAll(); } catch { }
                };

                _activeNotifications.Add(notification);
                notification.BeginAnimation();
            }
            catch
            {
                // Notification failed silently – hotkey still works
            }
        }

        public static string[] GetMonitorNames()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var names = new string[screens.Length];
                for (int i = 0; i < screens.Length; i++)
                {
                    var s = screens[i];
                    string primary = s.Primary ? " (Primary)" : "";
                    names[i] = $"Monitor {i + 1} – {s.Bounds.Width}×{s.Bounds.Height}{primary}";
                }
                return names;
            }
            catch
            {
                return new[] { "Primary Monitor" };
            }
        }

        public static int GetMonitorCount()
        {
            try { return System.Windows.Forms.Screen.AllScreens.Length; }
            catch { return 1; }
        }

        private static System.Windows.Forms.Screen? GetTargetScreen()
        {
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                if (_monitorIndex >= 0 && _monitorIndex < screens.Length)
                    return screens[_monitorIndex];
                return System.Windows.Forms.Screen.PrimaryScreen;
            }
            catch
            {
                return null;
            }
        }

        private static void RepositionAll()
        {
            var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
            if (visible.Count == 0) return;

            var screen = GetTargetScreen();
            if (screen == null) return;

            double centerY = screen.WorkingArea.Top + screen.WorkingArea.Height / 2;
            double totalStackH = visible.Sum(n => Math.Max(n.ActualHeight, 60) + 8);
            double startY = Math.Max(screen.WorkingArea.Top + 8, centerY - totalStackH / 2);

            foreach (var n in visible)
            {
                try { n.AnimateTop(startY); } catch { }
                startY += Math.Max(n.ActualHeight, 60) + 8;
            }
        }
    }
}
