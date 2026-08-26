using System;
using System.Collections.Generic;
using System.Linq;

namespace TenzoraX
{
    public static class NotificationManager
    {
        public const string CornerTopRight = "TopRight";
        public const string CornerTopLeft = "TopLeft";
        public const string CornerBottomRight = "BottomRight";
        public const string CornerBottomLeft = "BottomLeft";

        private static readonly List<NotificationWindow> _activeNotifications = new();
        private static bool _enabled = true;
        private static double _duration = 5.0;
        private static double _savedWidth = 360;
        private static double _savedPosX = -1;
        private static double _savedPosY = -1;

        /// <summary>Anker-Ecke der Benachrichtigung (aus den Einstellungen).</summary>
        public static string Corner { get; set; } = CornerTopRight;

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

        /// <summary>Setzt die benutzerdefinierte Drag-Position zurück, damit wieder die Ecke gilt.</summary>
        public static void ResetSavedPosition()
        {
            _savedPosX = -1;
            _savedPosY = -1;
        }

        public static void Show(string combo, string action)
        {
            if (!_enabled) return;

            try
            {
                var primary = GetPrimaryScreen();
                if (primary == null)
                {
                    App.LogApp("Notification: kein primärer Bildschirm gefunden");
                    return;
                }

                var workArea = primary.WorkingArea;

                double notifWidth = Math.Clamp(_savedWidth, 250, workArea.Width - 40);
                var notification = new NotificationWindow(combo, action, _duration, notifWidth);
                notification.Show();
                notification.Opacity = 0;

                notification.Closed += (s, e) =>
                {
                    _activeNotifications.Remove(notification);
                    // Nur wenn der Nutzer die Benachrichtigung wirklich gezogen hat,
                    // wird die Position gespeichert – sonst gilt weiterhin die Ecke.
                    if (notification.WasDragged)
                    {
                        _savedPosX = notification.Left;
                        _savedPosY = notification.Top;
                    }
                    try { RepositionAll(); } catch { }
                };

                _activeNotifications.Add(notification);
                RepositionAll();
                notification.BeginAnimation();
            }
            catch (Exception ex)
            {
                App.LogApp("Notification-Fehler: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static System.Windows.Forms.Screen? GetPrimaryScreen()
        {
            try { return System.Windows.Forms.Screen.PrimaryScreen; }
            catch (Exception ex)
            {
                App.LogApp("GetPrimaryScreen-Fehler: " + ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Positioniert alle sichtbaren Benachrichtigungen: die neueste an der
        /// gewählten Ecke (bzw. der gespeicherten Drag-Position), ältere daneben
        /// untereinander – oben nach unten, unten nach oben gestapelt.
        /// </summary>
        private static void RepositionAll()
        {
            var primary = GetPrimaryScreen();
            if (primary == null) return;

            var visible = _activeNotifications.Where(n => n.IsVisible).ToList();
            if (visible.Count == 0) return;

            var workArea = primary.WorkingArea;
            double notifWidth = Math.Clamp(_savedWidth, 250, workArea.Width - 40);

            // Gespeicherte Drag-Position (falls gültig): benutzerdefinierter Anker
            double firstH = Math.Max(visible[0].ActualHeight, 70);
            bool useSaved = _savedPosX >= workArea.Left && _savedPosX + notifWidth <= workArea.Right &&
                            _savedPosY >= workArea.Top && _savedPosY + firstH <= workArea.Bottom;

            const double margin = 16;
            const double gap = 8;

            // Neueste zuerst (zuletzt hinzugefügt) – sie sitzt am Anker
            visible.Reverse();

            bool topAnchor = Corner is CornerTopLeft or CornerTopRight;
            double y = useSaved
                ? _savedPosY
                : topAnchor ? workArea.Top + margin : workArea.Bottom - margin;
            int dir = topAnchor ? 1 : -1;

            foreach (var n in visible)
            {
                double h = Math.Max(n.ActualHeight, 70);
                double left = useSaved
                    ? _savedPosX
                    : (Corner == CornerBottomLeft || Corner == CornerTopLeft)
                        ? workArea.Left + margin
                        : workArea.Right - notifWidth - margin;

                try
                {
                    n.Left = left;
                    n.Top = dir > 0 ? y : y - h;
                }
                catch { }
                y += dir * (h + gap);
            }
        }
    }
}
