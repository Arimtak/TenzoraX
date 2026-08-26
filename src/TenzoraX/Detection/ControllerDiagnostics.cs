using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TenzoraX.Detection
{
    /// <summary>
    /// Zentrale Diagnose-Schicht: führt alle Erkennungs-Provider aus, entfernt
    /// Duplikate (DetectionMerge) und erzeugt den ausführlichen Diagnosebericht
    /// für die UI und die Kommandozeile (--diagnose).
    /// </summary>
    public static class ControllerDiagnostics
    {
        public sealed class Result
        {
            /// <summary>Zusammengeführte Geräte (dedupliziert).</summary>
            public List<DetectedDeviceInfo> Merged { get; } = new();
            /// <summary>Alle Rohbefunde je API inklusive Mehrfach-Sichtungen.</summary>
            public List<DetectedDeviceInfo> Findings { get; } = new();
            /// <summary>Fehlermeldungen einzelner Provider (leer = alles ok).</summary>
            public List<string> ProviderErrors { get; } = new();
        }

        // Reihenfolge = Anreicherungspriorität: HID liefert Name/Transport,
        // DirectInput Caps/GUIDs, WinMM Slots/Achsenzahlen, RawInput Pfade.
        private static readonly IControllerProvider[] Providers =
        {
            HidControllerProvider.Instance,
            new DirectInputControllerProvider(),
            new WinMMControllerProvider(),
            new RawInputControllerProvider()
        };

        public static Result Collect()
        {
            var result = new Result();

            foreach (var provider in Providers)
            {
                List<DetectedDeviceInfo> findings;
                try
                {
                    findings = provider.Enumerate();
                }
                catch (Exception ex)
                {
                    result.ProviderErrors.Add(provider.ApiName + ": " + ex.Message);
                    continue;
                }
                foreach (var f in findings)
                {
                    result.Findings.Add(f);
                }
                DetectionMerge.MergeInto(result.Merged, findings);
            }

            // XInput separat: Slots tragen keine VID/PID/Instance. Zuordnung nach dem
            //selben Heuristik-Muster wie im ControllerManager: bevorzugt Microsoft-HID-
            //Geräte (VID 045E), sonst noch nicht zugeordnete Kandidaten.
            var xinputFindings = result.Findings.Where(f => f.Api == ControllerApi.XInput).ToList();
            var claimedForXinput = new HashSet<DetectedDeviceInfo>();
            foreach (var xi in xinputFindings)
            {
                DetectedDeviceInfo? match =
                    result.Merged.FirstOrDefault(m => !m.SeenApis.Contains(ControllerApi.XInput)
                                                      && m.Vid == 0x045E && claimedForXinput.Add(m))
                    ?? result.Merged.FirstOrDefault(m => m.SeenApis.Count == 1
                                                         && m.SeenApis[0] == ControllerApi.DirectInput
                                                         && claimedForXinput.Add(m));

                if (match != null)
                {
                    if (!match.SeenApis.Contains(ControllerApi.XInput)) match.SeenApis.Add(ControllerApi.XInput);
                    if (!xi.SeenApis.Contains(ControllerApi.XInput)) xi.SeenApis.Add(ControllerApi.XInput);
                    DetectionMerge.Enrich(match, xi);
                }
                else
                {
                    if (!xi.SeenApis.Contains(ControllerApi.XInput)) xi.SeenApis.Add(ControllerApi.XInput);
                    result.Merged.Add(xi);   // ehrlich lassen: nur über XInput sichtbar
                }
            }

            return result;
        }

        /// <summary>
        /// Formatiert den vollständigen Diagnosebericht (Deutsch).
        /// Enthält immer auch die Rohbefunde je Schnittstelle – so lässt sich erkennen,
        /// ob Windows ein Gerät über HID/DirectInput/Raw Input sieht, obwohl es nicht
        /// als nutzbarer Controller erscheint.
        /// </summary>
        public static string BuildReport(Result r)
        {
            var sb = new StringBuilder(4096);
            sb.AppendLine("=== TenzoraX Controller-Diagnose ===");
            sb.AppendLine("Zeit:      " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("Version:   " + AppVersion.Current);

            if (r.ProviderErrors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Provider-Fehler:");
                foreach (var err in r.ProviderErrors)
                    sb.AppendLine("  - " + err);
            }

            sb.AppendLine();
            sb.AppendLine($"Zusammengeführte Geräte: {r.Merged.Count}");
            int idx = 1;
            foreach (var d in r.Merged)
            {
                AppendDevice(sb, idx++, d);
            }

            sb.AppendLine("--- Rohbefunde je Schnittstelle ---");
            foreach (ControllerApi api in Enum.GetValues(typeof(ControllerApi)))
            {
                var list = r.Findings.Where(f => f.Api == api).ToList();
                sb.AppendLine($"[{ControllerApiNames.Of(api)}] Befunde: {list.Count}");
                foreach (var f in list)
                {
                    string label = !string.IsNullOrEmpty(f.Name) ? f.Name
                                 : !string.IsNullOrEmpty(f.DevicePath) ? f.DevicePath
                                 : "(ohne Namen)";
                    sb.AppendLine($"   - {label}" +
                                  (!string.IsNullOrEmpty(f.VidPid) ? " | " + f.VidPid : "") +
                                  (!string.IsNullOrEmpty(f.DeviceInstanceId) ? " | " + f.DeviceInstanceId : ""));
                }
            }

            if (r.Merged.Count == 0)
            {
                sb.AppendLine();
                sb.AppendLine("Hinweis: Es wurde kein Gamecontroller gefunden.");
                sb.AppendLine("Prüfe, ob Windows den Controller überhaupt sieht");
                sb.AppendLine("(Gerätemanager / joy.cpl) und ob er mit einem anderen");
                sb.AppendLine("Prozess exklusiv belegt ist (z.B. Steam-Input).");
            }
            return sb.ToString();
        }

        private static void AppendDevice(StringBuilder sb, int index, DetectedDeviceInfo d)
        {
            sb.AppendLine("------------------------------------------------------------");
            sb.AppendLine($"Controller {index}:");
            sb.AppendLine("  Name:               " + (string.IsNullOrEmpty(d.Name) ? "(unbekannt)" : d.Name));
            sb.AppendLine("  Hersteller:         " + (string.IsNullOrEmpty(d.Manufacturer) ? "(unbekannt)" : d.Manufacturer));
            sb.AppendLine("  VID/PID:            " + (string.IsNullOrEmpty(d.VidPid) ? "-" : d.VidPid));
            sb.AppendLine("  Verbindung:         " + d.Transport);
            var apis = d.SeenApis.Select(ControllerApiNames.Of).ToList();
            sb.AppendLine("  APIs:               " + (apis.Count > 0 ? string.Join(", ", apis) : "-"));
            string guid = !string.IsNullOrEmpty(d.GuidInstance) ? d.GuidInstance : d.GuidProduct;
            sb.AppendLine("  GUID:               " + (guid.Length > 0 ? guid : "-"));
            sb.AppendLine("  Device Instance ID: " + (string.IsNullOrEmpty(d.DeviceInstanceId) ? "-" : d.DeviceInstanceId));
            if (!string.IsNullOrEmpty(d.DevicePath))
                sb.AppendLine("  Gerätepfad:         " + d.DevicePath);
            sb.AppendLine($"  Achsen: {(d.Axes > 0 ? d.Axes.ToString() : "?")}"
                          + $" | Tasten: {(d.Buttons > 0 ? d.Buttons.ToString() : "?")}"
                          + $" | POV/Hat: {(d.Povs > 0 ? d.Povs.ToString() : "?")}");
            if (d.UsagePage != 0)
                sb.AppendLine($"  HID-Usage:          Seite 0x{d.UsagePage:X4}, Usage 0x{d.Usage:X4}");
            sb.AppendLine();
        }
    }
}
