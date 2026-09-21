using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Ein einzelner Eingabe-Endpunkt eines Pads, so wie Windows ihn führt.
    /// Die Sicht stammt aus der laufenden Anwendung – sie kann also freigegebene
    /// Geräte sehen, die andere Programme (Steam, Spiele) nicht sehen.
    /// </summary>
    public sealed class InputEndpoint
    {
        /// <summary>Anzeigename des Endpunkts (Produktname aus DirectInput).</summary>
        public string Label { get; init; } = "";

        /// <summary>Kennzahlen des Endpunkts (Achsen/Tasten/POV, HID-Usage, VID/PID).</summary>
        public string Detail { get; init; } = "";

        /// <summary>Windows-Instanz-ID, sofern DirectInput sie liefert.</summary>
        public string InstanceId { get; init; } = "";

        /// <summary>True, wenn der Endpunkt echte Eingaben liefert (kein leeres Sammelgerät).</summary>
        public bool HasInput { get; init; }

        /// <summary>VID/PID des Endpunkts – damit lassen sich abgeschaltete Geschwister finden.</summary>
        public string VidPid { get; init; } = "";
    }

    /// <summary>
    /// Eine vom HID-Zweig gefundene Gamepad-Schnittstelle. Sie liefert den
    /// Windows-Gerätepfad, den DirectInput selbst nicht herausgibt.
    /// </summary>
    public sealed class HidInterface
    {
        public string VidPid { get; init; } = "";
        public string InstanceId { get; init; } = "";
    }

    /// <summary>Momentaufnahme aller Eingabe-Endpunkte, die Windows dem Pad zuordnet.</summary>
    public sealed class InputEndpointSnapshot
    {
        /// <summary>Belegte XInput-Slots (0-3) – das ist der Weg, den Spiele lesen.</summary>
        public List<int> OccupiedXInputSlots { get; } = new();

        /// <summary>Alle DirectInput-Gamecontroller, die Windows gerade führt.</summary>
        public List<InputEndpoint> DirectInputEndpoints { get; } = new();

        /// <summary>
        /// Schnittstellen desselben Pads, die in Windows deaktiviert sind. Sie liefern
        /// keine Eingaben mehr und tauchen deshalb weder im Windows-Panel noch bei
        /// Steam auf — hier werden sie trotzdem gezeigt, damit nachvollziehbar bleibt,
        /// was abgeschaltet wurde.
        /// </summary>
        public List<PhysicalDeviceResolver.DeviceStateInfo> DisabledInterfaces { get; } = new();

        /// <summary>HidHide-Zustand: installiert? Cloaking aktiv? Wie viele Geräte versteckt?</summary>
        public bool HidHideInstalled { get; set; }
        public bool CloakActive { get; set; }
        public int HiddenDeviceCount { get; set; }

        /// <summary>True, wenn der HidHide-Zustand überhaupt abgefragt werden konnte.</summary>
        public bool HidHideStateKnown { get; set; } = true;

        public int DirectInputCount => DirectInputEndpoints.Count;

        /// <summary>DirectInput-Endpunkte mit echten Eingaben (ohne leere Sammelgeräte).</summary>
        public int RealGamepadCount => DirectInputEndpoints.Count(e => e.HasInput);

        /// <summary>Zeilen, die das Windows-Panel „Gamecontroller“ anzeigen würde.</summary>
        public int PanelRowEstimate => InputEndpoints.PanelRowEstimate(OccupiedXInputSlots.Count, DirectInputCount);

        /// <summary>Controller, die Steam und andere SDL-Launcher zählen würden (abgeleitet).</summary>
        public int LauncherCountEstimate => InputEndpoints.LauncherCountEstimate(OccupiedXInputSlots.Count, RealGamepadCount);

        /// <summary>True, wenn dasselbe Pad gleichzeitig über XInput und als HID-Gamepad sichtbar ist.</summary>
        public bool DuplicateSuspected => InputEndpoints.DuplicateSuspected(OccupiedXInputSlots.Count, RealGamepadCount);
    }

    /// <summary>
    /// Sammelt live, welche Eingabe-Endpunkte Windows vom angeschlossenen Pad sieht,
    /// und rechnet daraus aus, was das Windows-Panel und Steam daraus machen.
    ///
    /// Warum das nützlich ist: 8BitDo-Pads und andere melden sich als ZWEI Geräte –
    /// einmal XInput (das lesen Spiele) und einmal als HID-Gamepad (das führt
    /// DirectInput, also auch das Panel und Steam). Das ist die Ursache dafür, dass
    /// derselbe Controller bei Steam doppelt auftaucht.
    ///
    /// Die Rechenregeln sind bewusst rein und damit testbar: sie bekommen nur Zahlen
    /// übergeben. Aufbau, Namen und Texte sind Deutsch (Projektkonvention).
    /// </summary>
    public static class InputEndpoints
    {
        /// <summary>Frische Momentaufnahme aus der Sicht dieser Anwendung.</summary>
        public static InputEndpointSnapshot Collect(bool includeHidHideState = true)
        {
            var snapshot = new InputEndpointSnapshot();

            try
            {
                snapshot.OccupiedXInputSlots.AddRange(XInputControllerProvider.GetOccupiedSlots());
            }
            catch (Exception ex)
            {
                App.LogApp("Endpunkte: XInput-Abfrage fehlgeschlagen: " + ex.Message);
            }

            var hidInterfaces = CollectHidInterfaces();
            try
            {
                foreach (var device in DirectInputControllerProvider.Enumerate(verboseLogging: false))
                {
                    bool hasInput = IsRealGamepad(device.UsagePage, device.Usage, device.Axes, device.Buttons, device.Povs);

                    // DirectInput liefert keinen Gerätepfad – der kommt aus dem HID-Zweig.
                    string instanceId = device.DeviceInstanceId ?? "";
                    if (string.IsNullOrEmpty(instanceId))
                        instanceId = MatchHidInstanceId(device.VidPid, hidInterfaces);

                    snapshot.DirectInputEndpoints.Add(new InputEndpoint
                    {
                        Label = string.IsNullOrWhiteSpace(device.Name) ? "(ohne Namen)" : device.Name,
                        Detail = Describe(device),
                        InstanceId = instanceId,
                        HasInput = hasInput,
                        VidPid = device.VidPid ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                App.LogApp("Endpunkte: DirectInput-Abfrage fehlgeschlagen: " + ex.Message);
            }

            // Abgeschaltete Schnittstellen desselben Pads mitführen (sie fehlen in
            // DirectInput, im Panel und bei Steam – sichtbar sind sie nur so).
            var vidPids = new List<string>();
            foreach (var endpoint in snapshot.DirectInputEndpoints)
                if (!string.IsNullOrEmpty(endpoint.VidPid)) vidPids.Add(endpoint.VidPid);
            foreach (var hid in hidInterfaces)
                if (!string.IsNullOrEmpty(hid.VidPid)) vidPids.Add(hid.VidPid);

            if (vidPids.Count > 0)
            {
                try
                {
                    snapshot.DisabledInterfaces.AddRange(
                        SelectDisabledInterfaces(PhysicalDeviceResolver.EnumerateDeviceStates(), vidPids));
                }
                catch (Exception ex)
                {
                    App.LogApp("Endpunkte: Gerätezustände nicht lesbar: " + ex.Message);
                }
            }

            if (includeHidHideState) FillHidHideState(snapshot);
            return snapshot;
        }

        /// <summary>HID-Gamepad-Schnittstellen (billig: ohne Descriptor-Details).</summary>
        private static List<HidInterface> CollectHidInterfaces()
        {
            var result = new List<HidInterface>();
            try
            {
                foreach (var hid in HidControllerProvider.Instance.EnumerateGamepads(
                             includeJoystickUsage: true, verboseLogging: false))
                {
                    result.Add(new HidInterface { VidPid = hid.VidPid ?? "", InstanceId = hid.InstanceId ?? "" });
                }
            }
            catch (Exception ex)
            {
                App.LogApp("Endpunkte: HID-Abfrage fehlgeschlagen: " + ex.Message);
            }
            return result;
        }

        private static void FillHidHideState(InputEndpointSnapshot snapshot)
        {
            try
            {
                string? cli = HidHideAutomation.FindCli();
                snapshot.HidHideInstalled = !string.IsNullOrEmpty(cli);
                if (!snapshot.HidHideInstalled)
                {
                    snapshot.CloakActive = false;
                    snapshot.HiddenDeviceCount = 0;
                    return;
                }

                snapshot.CloakActive = HidHideAutomation.IsCloakActive();
                snapshot.HiddenDeviceCount = HidHideAutomation.ListHiddenDevices().Count;
            }
            catch (Exception ex)
            {
                snapshot.HidHideStateKnown = false;
                App.LogApp("Endpunkte: HidHide-Zustand nicht lesbar: " + ex.Message);
            }
        }

        private static string Describe(DetectedDeviceInfo d)
        {
            var sb = new StringBuilder(96);
            sb.Append($"Eingaben: {d.Axes} Achsen, {d.Buttons} Tasten, {d.Povs} POV");
            if (d.UsagePage != 0 || d.Usage != 0)
                sb.Append($"   HID-Usage 0x{d.UsagePage:X4}/0x{d.Usage:X4}");
            if (!string.IsNullOrEmpty(d.VidPid))
                sb.Append("   " + d.VidPid);
            if (!string.IsNullOrEmpty(d.Transport) && !string.Equals(d.Transport, "Unknown", StringComparison.OrdinalIgnoreCase))
                sb.Append("   " + d.Transport);
            return sb.ToString();
        }

        // ============================================================
        //  Reine Rechenregeln (testbar)
        // ============================================================

        /// <summary>
        /// True, wenn der Endpunkt wirklich Eingaben liefert.
        ///
        /// Wichtig: Manche Pads melden zusätzliche Sammlungen (Multimedia-Usage 0x000C,
        /// herstellerdefinierte Usage 0xFF7A). DirectInput und das Windows-Panel zählen
        /// sie trotzdem als Controller – Spiele, Steam und dieser Bericht aber nicht.
        /// </summary>
        public static bool IsRealGamepad(ushort usagePage, ushort usage, int axes, int buttons, int povs)
        {
            // Generic-Desktop-Usage: Gamepad (0x05), Joystick (0x04), Multi-Axis (0x08)
            if (usagePage == 0x0001)
                return usage == 0x0004 || usage == 0x0005 || usage == 0x0008
                       || axes > 0 || buttons > 0 || povs > 0;

            // Andere Usage-Seite (z. B. 0x000C Multimedia, 0xFF7A Hersteller): kein Gamepad.
            if (usagePage != 0) return false;

            // Usage unbekannt: nach den gemeldeten Eingaben entscheiden.
            return axes > 0 || buttons > 0 || povs > 0;
        }

        /// <summary>
        /// Sucht den Windows-Gerätepfad zu einem DirectInput-Gerät.
        /// Zugeordnet wird über VID/PID – aber nur, wenn die Zuordnung eindeutig ist;
        /// bei mehreren gleichnamigen Schnittstellen bleibt der Pfad lieber leer als falsch.
        /// </summary>
        public static string MatchHidInstanceId(string? vidPid, IEnumerable<HidInterface>? candidates)
        {
            if (string.IsNullOrWhiteSpace(vidPid) || candidates == null) return "";

            string treffer = "";
            int anzahl = 0;
            foreach (var c in candidates)
            {
                if (c == null || string.IsNullOrEmpty(c.InstanceId)) continue;
                if (!string.Equals(c.VidPid, vidPid, StringComparison.OrdinalIgnoreCase)) continue;
                anzahl++;
                treffer = c.InstanceId;
            }
            return anzahl == 1 ? treffer : "";
        }

        /// <summary>
        /// Wählt aus allen Geräteknoten die deaktivierten aus, die zu einem der
        /// bekannten VID/PID-Werte des Pads gehören. Rein und damit testbar.
        /// </summary>
        public static List<PhysicalDeviceResolver.DeviceStateInfo> SelectDisabledInterfaces(
            IEnumerable<PhysicalDeviceResolver.DeviceStateInfo>? all,
            IEnumerable<string>? vidPids)
        {
            var result = new List<PhysicalDeviceResolver.DeviceStateInfo>();
            if (all == null || vidPids == null) return result;

            var wanted = new List<string>();
            foreach (var v in vidPids)
                if (!string.IsNullOrWhiteSpace(v) && !wanted.Contains(v, StringComparer.OrdinalIgnoreCase))
                    wanted.Add(v);
            if (wanted.Count == 0) return result;

            foreach (var node in all)
            {
                if (node == null || node.State != PhysicalDeviceResolver.DeviceState.Disabled) continue;
                if (string.IsNullOrEmpty(node.InstanceId)) continue;
                foreach (var v in wanted)
                {
                    if (node.InstanceId.Contains(v, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Add(node);
                        break;
                    }
                }
            }
            return result;
        }

        /// <summary>Zeilen im Windows-Panel: je DirectInput-Gerät eine, plus je belegtem XInput-Slot eine.</summary>
        public static int PanelRowEstimate(int occupiedXInputSlots, int directInputDevices) =>
            Math.Max(0, occupiedXInputSlots) + Math.Max(0, directInputDevices);

        /// <summary>
        /// Controller, die Steam/andere SDL-Launcher zählen: je belegtem XInput-Slot einer,
        /// plus je HID-Gamepad mit echten Eingaben einer. (Leere Sammelgeräte ignorieren sie.)
        /// </summary>
        public static int LauncherCountEstimate(int occupiedXInputSlots, int realGamepads) =>
            Math.Max(0, occupiedXInputSlots) + Math.Max(0, realGamepads);

        /// <summary>True, wenn dasselbe Pad über XInput UND als HID-Gamepad auftaucht – das Duplikat.</summary>
        public static bool DuplicateSuspected(int occupiedXInputSlots, int realGamepads) =>
            occupiedXInputSlots > 0 && realGamepads > 0;

        // ============================================================
        //  Textfassung für die Oberfläche und die --diagnose-Datei
        // ============================================================

        public static string Format(InputEndpointSnapshot snapshot)
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("=== Eingabe-Endpunkte (live) ===");
            sb.AppendLine("Stand:     " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            sb.AppendLine("XInput (das lesen Spiele und Steam-Input):");
            for (int slot = 0; slot < 4; slot++)
                sb.AppendLine($"  Slot {slot}:   " + (snapshot.OccupiedXInputSlots.Contains(slot) ? "BELEGT" : "leer"));
            sb.AppendLine();

            sb.AppendLine("DirectInput (dieselbe Liste führt das Windows-Panel \"Gamecontroller\" und Steam als Generic Gamepad):");
            if (snapshot.DirectInputEndpoints.Count == 0)
            {
                sb.AppendLine("  (kein DirectInput-Gerät)");
            }
            else
            {
                int index = 0;
                foreach (var endpoint in snapshot.DirectInputEndpoints)
                {
                    index++;
                    sb.AppendLine($"  [{index}] {endpoint.Label}" + (endpoint.HasInput ? "" : "   <-- keine Eingaben"));
                    sb.AppendLine("      " + endpoint.Detail);
                    if (!string.IsNullOrEmpty(endpoint.InstanceId))
                        sb.AppendLine("      Instanz: " + endpoint.InstanceId);
                }
            }            sb.AppendLine();
            sb.AppendLine("In Windows deaktivierte Schnittstellen desselben Pads:");
            if (snapshot.DisabledInterfaces.Count == 0)
            {
                sb.AppendLine("  (keine)");
            }
            else
            {
                int index = 0;
                foreach (var node in snapshot.DisabledInterfaces)
                {
                    index++;
                    sb.AppendLine($"  [{index}] " + (string.IsNullOrEmpty(node.FriendlyName) ? "(ohne Namen)" : node.FriendlyName));
                    sb.AppendLine("      " + node.InstanceId);
                }
                sb.AppendLine("  Diese Schnittstellen sind in Windows abgeschaltet (Problemcode 22). Sie liefern");
                sb.AppendLine("  keine Eingaben und tauchen deshalb weder im Windows-Panel noch bei Steam auf.");
                sb.AppendLine("  Rückweg: im Geräte-Manager aktivieren oder Enable-PnpDevice.");
            }
            sb.AppendLine();
            sb.AppendLine("HidHide: " + DescribeHidHide(snapshot));
            sb.AppendLine();

            sb.AppendLine("Auswertung:");
            sb.AppendLine($"  Windows-Panel:  {snapshot.PanelRowEstimate} Zeile(n) = {snapshot.DirectInputCount} DirectInput-Gerät(e)"
                          + $" + {snapshot.OccupiedXInputSlots.Count} belegte(r) XInput-Slot(s)");
            sb.AppendLine($"  Steam/Launcher: {snapshot.LauncherCountEstimate} Controller"
                          + "  (abgeleitet: belegte XInput-Slots + DirectInput-Geräte mit echten Eingaben)");

            sb.AppendLine();
            if (snapshot.DuplicateSuspected)
            {
                sb.AppendLine("  >> Dasselbe Pad meldet sich zweimal: über XInput (" + string.Join(", ", snapshot.OccupiedXInputSlots)
                              + ") UND als HID-Gamepad.");
            }
            else
            {
                sb.AppendLine("  >> Das Pad ist über einen einzigen Eingabeweg sichtbar.");
            }

            return sb.ToString();
        }

        private static string DescribeHidHide(InputEndpointSnapshot snapshot)
        {
            if (!snapshot.HidHideStateKnown)
                return "Zustand nicht lesbar (CLI-Aufruf fehlgeschlagen).";

            if (!snapshot.HidHideInstalled)
                return "nicht installiert – es ist nichts vor Programmen versteckt. "
                     + "Diese Ansicht entspricht damit der Sicht von Steam und Spielen.";

            string state = snapshot.CloakActive
                ? $"Cloaking aktiv, {snapshot.HiddenDeviceCount} Gerät(e) versteckt."
                : "installiert, Cloaking aus – es ist nichts versteckt.";

            return state + (snapshot.CloakActive
                ? " Achtung: Die App steht in der Ausnahmeliste und sieht hier mehr als Steam und Spiele."
                : "");
        }
    }
}
