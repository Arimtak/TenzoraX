using System;
using System.Collections.Generic;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Wählt aus den Windows-Gerätenamen den echten Produktnamen aus. Rein rechnend
    /// (ohne SetupAPI), damit die Regeln prüfbar bleiben.
    ///
    /// Hintergrund: Raw Input meldet zu einem Gerät nur VID/PID. Ohne Auflösung
    /// landet dann „VID_2DC8&amp;PID_3024“ in der Controller-Liste, obwohl Windows für
    /// dieselbe Hardware einen Gerätenamen kennt – notfalls beim Elternknoten, denn
    /// zu einem XInput-Pad legt Windows eine HID-Fassade mit eigenem (Treiber-)Namen an.
    /// </summary>
    internal static class DeviceNameResolver
    {
        /// <summary>
        /// Bester verfügbarer Produktname: eigener Name → Name des Elternknotens
        /// (physische Instanz) → bekannter Controller-/Herstellername. Leer, wenn
        /// nichts davon greift (der Aufrufer behält dann seinen Rückfall).
        /// </summary>
        public static string PickProductName(
            string? ownFriendly,
            string? ownDesc,
            IReadOnlyList<(string Friendly, string Desc)>? ancestors,
            ushort vid,
            ushort pid)
        {
            if (IsRealProductName(ownFriendly)) return ownFriendly!.Trim();
            if (IsRealProductName(ownDesc)) return ownDesc!.Trim();

            if (ancestors != null)
            {
                foreach (var ancestor in ancestors)
                {
                    if (IsRealProductName(ancestor.Friendly)) return ancestor.Friendly.Trim();
                    if (IsRealProductName(ancestor.Desc)) return ancestor.Desc.Trim();
                }
            }

            // Windows kennt für dieses Gerät nur Treiber-Platzhalter: dann lieber ein
            // sprechender Familienname als die rohe VID/PID.
            string known = DeviceHelpers.GetKnownControllerName(vid, pid);
            if (!string.IsNullOrEmpty(known)) return known;

            string vendor = DeviceHelpers.GetKnownVendorName(vid);
            return !string.IsNullOrEmpty(vendor) ? vendor + " Gamepad" : "";
        }

        /// <summary>
        /// True, wenn der Text ein echter Produktname ist – also weder leer noch ein
        /// VID/PID-Platzhalter noch eine generische Windows-Beschreibung
        /// („HID-konformes Benutzersteuergerät“, „USB-Eingabegerät“, „XINPUT-kompatibles
        /// HID-Gerät“, „Wireless Controller“ ohne Herstellerbezug).
        /// </summary>
        public static bool IsRealProductName(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            string name = text.Trim();

            // Platzhalter, wie ihn die App selbst erzeugt, wenn kein Name bekannt ist.
            if (name.StartsWith("VID_", StringComparison.OrdinalIgnoreCase)) return false;

            return !DeviceHelpers.LooksLikeGenericGamepad(name)
                   && !DeviceHelpers.IsDriverPlaceholderName(name)
                   && !IsInfrastructureName(name);
        }

        /// <summary>
        /// Namen von Geräten, die keine Controller sind, aber im Gerätebaum über dem
        /// Pad hängen: USB-Hubs, Verbundgeräte, Bluetooth-Dienste. Ohne diesen Filter
        /// hieß ein 8BitDo-Pad nach dem Aufstieg im Baum „Generischer USB-Hub“
        /// (am 26.10.2026 mit echten Gerätenamen nachgemessen).
        /// </summary>
        public static bool IsInfrastructureName(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return true;
            string t = text.Trim().ToLowerInvariant();
            return t.Contains("hub")
                   || t.Contains("verbundgerät")
                   || t.Contains("composite")
                   || t.Contains("dienst")
                   || t.Contains("service")
                   || t.Contains("enumerator");
        }
    }
}
