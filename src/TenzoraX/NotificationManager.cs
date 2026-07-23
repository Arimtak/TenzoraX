using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TenzoraX
{
    public static class NotificationManager
    {
        private static readonly List<NotificationWindow> _activeNotifications = new();
        private static bool _enabled = true;
        private static double _duration = 5.0;
        private static double _savedWidth = 360;
        private static double _savedPosX = -1;
        private static double _savedPosY = -1;
        private static string LogPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "TenzoraX", "notification.log");

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

        public static double NotificationWidth
        {
            get => _savedWidth;
            set => _savedWidth = Math.Clamp(value, 250, 600);
        }

        public static double SavedPosX
        {
            get => _savedPosX;
            set => _savedPosX = value;
        }

        public static double SavedPosY
        {
            get => _savedPosY;
            set => _savedPosY = value;
        }

        public static void Show(string combo, string action)
        {
            if (!_enabled) return;

            try
            {
                Log("Show() called: " + combo + " → " + action);

                var primary = GetPrimaryScreen();
                if (primary == null)
                {
                    Log("ERROR: no primary screen found");
                    return;
                }

                var workArea = primary.WorkingArea;
                Log($"Primary working area: {workArea.Width}x{workArea.Height} at ({workArea.Left},{workArea.Top})");

                double notifWidth = Math.Clamp(_savedWidth, 250, workArea.Width - 40);
                var notification = new NotificationWindow(combo, action, _duration, notifWidth);
                notification.Show();
                notification.Opacity = 0;

                double notifHeight = Math.Max(notification.ActualHeight, 70);
                Log($"Notification size: {notifWidth}x{notifHeight}");

                double left, top;

                if (_savedPosX >= workArea.Left && _savedPosX + notifWidth <= workArea.Right &&
                    _savedPosY >= workArea.Top && _savedPosY + notifHeight <= workArea.Bottom)
                {
                    left = _savedPosX;
                    top = _savedPosY;
                    Log("Using saved position");
                }
                else
                {
                    left = workArea.Left + 16;
                    top = workArea.Top + (workArea.Height - notifHeight) / 2;
                    Log("Using default center-left position");
                }

                var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
                double totalStackH = visible.Sum(n => Math.Max(n.ActualHeight, 70) + 8);
                top -= totalStackH;
                if (top < workArea.Top + 4) top = workArea.Top + 4;

                notification.Top = top;
                notification.Left = left;
                Log($"Final position: X={left} Y={top}");

                notification.Closed += (s, e) =>
                {
                    _activeNotifications.Remove(notification);
                    _savedPosX = notification.Left;
                    _savedPosY = notification.Top;
                    Log($"Position saved: X={_savedPosX} Y={_savedPosY}");
                    try { RepositionAll(); } catch (Exception ex) { Log("RepositionAll error: " + ex.Message); }
                };

                _activeNotifications.Add(notification);
                Log("Starting animation");
                notification.BeginAnimation();
                Log("Animation started");
            }
            catch (Exception ex)
            {
                Log("Show() ERROR: " + ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace);
            }
        }

        private static System.Windows.Forms.Screen? GetPrimaryScreen()
        {
            try { return System.Windows.Forms.Screen.PrimaryScreen; }
            catch (Exception ex)
            {
                Log("GetPrimaryScreen() ERROR: " + ex.Message);
                return null;
            }
        }

        private static void RepositionAll()
        {
            var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
            if (visible.Count == 0) return;

            var primary = GetPrimaryScreen();
            if (primary == null) return;

            var workArea = primary.WorkingArea;
            double totalStackH = visible.Sum(n => Math.Max(n.ActualHeight, 70) + 8);
            double startY = workArea.Top + (workArea.Height - totalStackH) / 2;
            if (startY < workArea.Top + 4) startY = workArea.Top + 4;

            foreach (var n in visible)
            {
                try { n.AnimateTop(startY); } catch { }
                startY += Math.Max(n.ActualHeight, 70) + 8;
            }
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
