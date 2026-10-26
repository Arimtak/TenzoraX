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

        /// <summary>
        /// Kanonischer Schlüssel einer Geräte-Instanz für Identitätsvergleiche.
        ///
        /// Windows liefert dieselbe Instanz je nach Quelle unterschiedlich geschrieben:
        /// der SetupAPI-Pfad mit Großbuchstaben (`…\9&amp;8581B18&amp;…`), der Raw-Input-Pfad
        /// klein (`…\9&amp;8581b18&amp;…`), und aus Interface-Pfaden kommt `#` statt `\`.
        /// Instanz-IDs sind bei Windows grundsätzlich nicht case-sensitiv – ein Vergleich
        /// mit `==` führt deshalb zu Geister-Geräten (2026-10-26 im Verlauf beobachtet).
        /// </summary>
        internal static string NormalizeInstanceKey(string? instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return "";
            string key = instanceId.Trim();

            // Interface-Pfade ("\\\\?\\HID#VID_x&PID_y#…") auf die Instanz-ID-Form
            // bringen: Gerätepfad-Präfix weg, '#' als Trenner zu '\\'.
            if (key.StartsWith(@"\\?\", StringComparison.Ordinal)) key = key.Substring(4);
            else if (key.StartsWith(@"\??\", StringComparison.Ordinal)) key = key.Substring(4);

            return key.Replace('#', '\\').ToUpperInvariant();
        }

        /// <summary>Generische Gamepad-Beschreibungen (lokalisiert) erkennen.</summary>
        internal static bool LooksLikeGenericGamepad(string text)
        {
            string t = text.ToLowerInvariant();
            return t.Contains("game controller") || t.Contains("gamecontroller") || t.Contains("gamepad")
                || t.Contains("joystick") || t.Contains("hid-compliant") || t.Contains("spielsteuerung");
        }

        /// <summary>
        /// Treiber-Platzhalter von Windows statt eines Hersteller-Produktnamens
        /// (z.B. "XINPUT-kompatibles HID-Gerät", "HID-compliant game controller",
        /// "Wireless Controller"). Aufrufer prüft zusätzlich die VID, damit ein
        /// wirklich generischer Name eines Fremdherstellers unangetastet bleibt.
        /// </summary>
        internal static bool IsDriverPlaceholderName(string text)
        {
            if (string.IsNullOrEmpty(text)) return true;
            string t = text.Trim().ToLowerInvariant();
            // Exakter Vergleich für "Wireless Controller": so bleibt ein echter
            // Produktname wie "Xbox Elite Wireless Controller Series 2" erhalten.
            return t.Contains("xinput") || t.Contains("hid-compliant") || t.Contains("hid-konform")
                || t.Contains("input device") || t.Contains("eingabeger")
                || t == "wireless controller" || t == "drahtloscontroller";
        }

        /// <summary>
        /// Verständlicher Anzeigename für bekannte Gamepads. Windows meldet für
        /// XInput-Pads nur den Treibernamen ("XINPUT-kompatibles HID-Gerät" /
        /// "XINPUT compatible HID device"), Sony-Pads oft nur "Wireless Controller" –
        /// VID/PID ist dort die einzige verlässliche Quelle. Leer = unbekannt, der
        /// Aufrufer behält dann seinen Namen.
        ///
        /// Aufruf nur für bereits als Gamepad erkannte Geräte: die VIDs decken bei
        /// denselben Herstellern auch Mäuse, Tastaturen und anderes Zubehör ab.
        /// Unbekannte PIDs eines bekannten Herstellers bekommen bewusst einen
        /// neutralen Namen statt gar keinem.
        /// </summary>
        internal static string GetKnownControllerName(ushort vid, ushort pid)
        {
            switch (vid)
            {
                case 0x045E:   // Microsoft
                    return pid switch
                    {
                        0x028E or 0x028F or 0x0291 or 0x0719 => "Xbox 360 Controller",
                        _ => "Xbox-Controller"
                    };

                case 0x054C:   // Sony
                    return pid switch
                    {
                        0x0268 => "DualShock 3",
                        0x05C4 or 0x09CC => "DualShock 4",
                        0x0CE6 => "DualSense",
                        0x0DF2 => "DualSense Edge",
                        _ => "PlayStation-Controller"
                    };

                case 0x057E:   // Nintendo
                    return pid switch
                    {
                        0x2009 => "Switch Pro Controller",
                        0x2006 => "Joy-Con (links)",
                        0x2007 => "Joy-Con (rechts)",
                        _ => "Nintendo-Controller"
                    };

                default:
                    return "";
            }
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
