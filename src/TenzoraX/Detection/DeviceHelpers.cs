using System;
using Microsoft.Win32;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Reine Hilfsfunktionen der Geräteerkennung (VID/PID-Parsing, Namen, Transport).
    /// Von den Providern und vom ControllerManager gemeinsam genutzt.
    /// </summary>
    internal static class DeviceHelpers
    {
        /// <summary>
        /// Extrahiert VID/PID aus Geräte-Instanz-IDs und -Pfaden.
        /// Unterstützt beide Windows-Formate:
        ///  - klassisch:   "VID_2DC8&amp;PID_200B"
        ///  - Bluetooth-GATT: "…_DEV_VID&amp;022DC8_PID&amp;200B_…" (Namespace-Ziffer +
        ///    4 Hex-Ziffern; die letzten 4 Hexzeichen vor dem Trenner zählen).
        /// </summary>
        internal static bool TryParseVidPid(string text, out ushort vid, out ushort pid)
        {
            vid = 0;
            pid = 0;
            if (!TryParseHexToken(text, "VID_", out vid)) return false;
            if (!TryParseHexToken(text, "PID_", out pid)) return false;
            return vid != 0 || pid != 0;
        }

        /// <summary>Liest den Hex-Token nach einem Präfix ("VID_"/"VID&", "PID_"/"PID&").</summary>
        private static bool TryParseHexToken(string text, string underscorePrefix, out ushort value)
        {
            value = 0;
            int idx = text.IndexOf(underscorePrefix, System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                idx = text.IndexOf(underscorePrefix.Substring(0, 3) + "&", System.StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return false;

            // Alle unmittelbar folgenden Hexzeichen einsammeln
            int start = idx + 4;
            int end = start;
            while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
            if (end - start < 4) return false;

            // Mehr als 4 Ziffern (GATT-Namespace-Präfix): die letzten 4 zählen
            return ushort.TryParse(text.Substring(end - 4, 4),
                System.Globalization.NumberStyles.HexNumber, null, out value);
        }

        internal static string FormatVidPid(ushort vid, ushort pid) => $"VID_{vid:X4}&PID_{pid:X4}";

        /// <summary>Generische Gamepad-Beschreibungen (lokalisiert) erkennen.</summary>
        internal static bool LooksLikeGenericGamepad(string text)
        {
            string t = text.ToLowerInvariant();
            return t.Contains("game controller") || t.Contains("gamecontroller") || t.Contains("gamepad")
                || t.Contains("joystick") || t.Contains("hid-compliant") || t.Contains("spielsteuerung");
        }

        internal static string GetKnownVendorName(ushort vid)
        {
            return vid switch
            {
                0x2DC8 => "8BitDo",
                0x045E => "Microsoft",
                0x054C => "Sony",
                0x057E => "Nintendo",
                0x046D => "Logitech",
                0x1532 => "Razer",
                0x044F => "Thrustmaster",
                0x0F0D => "HORI",
                0x20D6 => "PowerA",
                _ => ""
            };
        }

        /// <summary>OEM-Anzeigename aus der Windows-Joystick-Registry (wird auch von joy.cpl verwendet).</summary>
        internal static string GetWinMmOemName(string regKey)
        {
            try
            {
                string oem = regKey;
                int slash = oem.IndexOf('\\');
                if (slash >= 0) oem = oem.Substring(0, slash);
                oem = oem.Trim();
                if (string.IsNullOrEmpty(oem)) return "";

                const string basePath = @"SYSTEM\CurrentControlSet\Control\MediaProperties\PrivateProperties\Joystick\OEM";
                using (var key = Registry.LocalMachine.OpenSubKey(basePath + "\\" + oem))
                {
                    if (key != null && key.GetValue("OEMName") is string name && !string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
                using (var key = Registry.LocalMachine.OpenSubKey(basePath + "\\" + oem.ToLowerInvariant()))
                {
                    if (key != null && key.GetValue("OEMName") is string name && !string.IsNullOrWhiteSpace(name))
                        return name.Trim();
                }
            }
            catch { }
            return "";
        }
    }
}
