using System;
using System.Runtime.InteropServices;

namespace TenzoraX.Effects
{
    /// <summary>
    /// Bildwiederholrate des Hauptbildschirms.
    ///
    /// <b>Nur für Log und Diagnose</b> – bewusst <i>nicht</i> als Maßstab für die Absicherung:
    /// gemessen am 13.09.2026 (Bildschirm mit 120 Hz) lieferte WPF je nach Zustand 33 bis 119
    /// Takte/s, auch ohne Effekt. Die Monitorrate taugt deshalb nicht als Referenz dafür, ob die
    /// Oberfläche mitkommt; dafür gelten die absoluten Schwellen in
    /// <see cref="BackgroundPerformancePolicy"/>. Im Log steht sie trotzdem, weil sie die
    /// gemessenen Takte einordnet.
    ///
    /// Der Wert wird einmal gelesen und gepuffert. Lässt er sich nicht ermitteln (Remotedesktop,
    /// Treiber ohne Angabe), bleibt er 0.
    /// </summary>
    internal static class DisplayRefresh
    {
        private const int VRefresh = 116;

        [DllImport("user32.dll", SetLastError = false)]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = false)]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);

        [DllImport("gdi32.dll", SetLastError = false)]
        private static extern int GetDeviceCaps(IntPtr hDc, int index);

        private static double? _hz;

        /// <summary>Bildwiederholrate in Hertz; 0, wenn sie nicht ermittelbar ist.</summary>
        public static double Hz => _hz ??= Read();

        private static double Read()
        {
            IntPtr dc = IntPtr.Zero;
            try
            {
                dc = GetDC(IntPtr.Zero);
                if (dc == IntPtr.Zero) return 0;

                int value = GetDeviceCaps(dc, VRefresh);
                return value > 1 ? value : 0;
            }
            catch
            {
                return 0;
            }
            finally
            {
                if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            }
        }
    }
}
