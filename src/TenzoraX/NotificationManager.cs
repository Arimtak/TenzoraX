using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

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

            var screen = GetTargetScreen();
            if (screen == null) return;

            var notification = new NotificationWindow(combo, action, _duration);
            notification.Show();
            notification.Opacity = 0;

            double baseLeft = screen.WorkingArea.Left + 16;
            double centerY = screen.WorkingArea.Top + screen.WorkingArea.Height / 2;
            double height = notification.ActualHeight;

            var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
            double totalStackH = visible.Sum(n => n.ActualHeight + 8);
            notification.Top = centerY - totalStackH - height / 2;
            notification.Left = baseLeft;

            notification.Closed += (s, e) =>
            {
                _activeNotifications.Remove(notification);
                RepositionAll();
            };

            _activeNotifications.Add(notification);
            notification.BeginAnimation();
        }

        public static string[] GetMonitorNames()
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

        public static int GetMonitorCount()
        {
            return System.Windows.Forms.Screen.AllScreens.Length;
        }

        private static System.Windows.Forms.Screen? GetTargetScreen()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (_monitorIndex >= 0 && _monitorIndex < screens.Length)
                return screens[_monitorIndex];
            return System.Windows.Forms.Screen.PrimaryScreen;
        }

        private static void RepositionAll()
        {
            var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
            if (visible.Count == 0) return;

            var screen = GetTargetScreen();
            if (screen == null) return;

            double centerY = screen.WorkingArea.Top + screen.WorkingArea.Height / 2;
            double totalStackH = visible.Sum(n => n.ActualHeight + 8);
            double startY = centerY - totalStackH / 2;

            foreach (var n in visible)
            {
                n.AnimateTop(startY);
                startY += n.ActualHeight + 8;
            }
        }
    }
}
