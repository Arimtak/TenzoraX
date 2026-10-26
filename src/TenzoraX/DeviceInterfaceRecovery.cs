using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using TenzoraX.Detection;

namespace TenzoraX
{
    /// <summary>
    /// Aktiviert eine in Windows deaktivierte Geräte-Schnittstelle (Problemcode 22)
    /// über Enable-PnpDevice. Läuft TenzoraX bereits als Administrator, fällt die
    /// UAC-Abfrage weg; sonst fragt Windows einmalig nach.
    ///
    /// Warum das Ergebnis am Gerätezustand gemessen wird: Ein Shell-Start mit
    /// „runas“ liefert keinen verlässlichen Exitcode. Nach dem Aufruf wird deshalb
    /// nachgesehen, ob Windows die Schnittstelle wirklich wieder führt.
    /// </summary>
    public static class DeviceInterfaceRecovery
    {
        /// <summary>Wie lange nach dem Aktivieren auf den Start gewartet wird.</summary>
        private const int StartTimeoutMs = 6000;

        /// <summary>Argumente für powershell.exe – Instanz-ID in einfachen Anführungszeichen.</summary>
        internal static string BuildEnableArguments(string instanceId)
        {
            return "-NoProfile -NonInteractive -WindowStyle Hidden -Command \"Enable-PnpDevice -InstanceId '"
                 + Escape(instanceId)
                 + "' -Confirm:$false\"";
        }

        /// <summary>Einfache Anführungszeichen verdoppeln (PowerShell-Quoting).</summary>
        internal static string Escape(string? instanceId) => (instanceId ?? "").Replace("'", "''");

        /// <summary>
        /// Wählt je blockierendem VID/PID die USB-Schnittstelle, die neu gestartet wird.
        ///
        /// Nur `USB\…&MI_…`-Knoten kommen infrage: Ein Neustart der HID-Sammlung allein
        /// löst den Stau nicht, und das Verbundgerät lässt sich nicht immer neu starten.
        /// Bevorzugt wird MI_01 (dort hängen die HID-Sammlungen der Tastatur/Receiver);
        /// Gamepad-Schnittstellen bleiben ausgespart, damit der Controller nicht kurz
        /// verschwindet. Rein und damit testbar.
        /// </summary>
        public static List<string> SelectRestartTargets(
            IEnumerable<PhysicalDeviceResolver.DeviceStateInfo>? nodes,
            IEnumerable<string>? vidPids)
        {
            var result = new List<string>();
            if (nodes == null || vidPids == null) return result;

            var wanted = new List<string>();
            foreach (var vidPid in vidPids)
            {
                if (string.IsNullOrWhiteSpace(vidPid)) continue;
                if (!wanted.Contains(vidPid, StringComparer.OrdinalIgnoreCase)) wanted.Add(vidPid);
            }
            if (wanted.Count == 0) return result;

            foreach (var vidPid in wanted)
            {
                string? best = null;
                foreach (var node in nodes)
                {
                    if (node == null || string.IsNullOrEmpty(node.InstanceId)) continue;
                    if (!node.InstanceId.Contains(vidPid, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!node.InstanceId.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase)) continue;
                    if (!node.InstanceId.Contains("&MI_", StringComparison.OrdinalIgnoreCase)) continue;
                    if (InputEndpoints.IsGamepadInterfaceName(node.FriendlyName)) continue;

                    if (best == null) best = node.InstanceId;
                    if (node.InstanceId.Contains("&MI_01", StringComparison.OrdinalIgnoreCase))
                    {
                        best = node.InstanceId;
                        break;
                    }
                }
                if (best != null) result.Add(best);
            }
            return result;
        }

        /// <summary>
        /// Startet die blockierenden USB-Schnittstellen neu und misst, ob DirectInput
        /// danach wieder schnell antwortet. Der Aufruf braucht Administratorrechte
        /// (TenzoraX läuft dafür bereits erhöht) und dauert im Staufall bis zu einer Minute.
        /// </summary>
        public static (bool Success, string Message) RestartStalledDevices(IReadOnlyList<string>? vidPids)
        {
            if (vidPids == null || vidPids.Count == 0)
                return (false, "Keine blockierenden Geräte bekannt.");
            if (!InputSimulator.IsRunningAsAdmin())
                return (false, "Administratorrechte erforderlich.");

            var targets = SelectRestartTargets(
                PhysicalDeviceResolver.EnumerateDeviceStates(presentOnly: true), vidPids);
            if (targets.Count == 0)
            {
                App.LogApp("DI-Stau: keine passende USB-Schnittstelle für " + string.Join(", ", vidPids) + " gefunden.");
                return (false, "Keine passende USB-Schnittstelle gefunden.");
            }

            foreach (var instanceId in targets)
            {
                try
                {
                    var psi = new ProcessStartInfo("pnputil.exe", "/restart-device \"" + instanceId + "\"")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };
                    using var process = Process.Start(psi);
                    if (process == null) continue;
                    process.WaitForExit(20000);
                    App.LogApp("DI-Stau: Schnittstelle neu gestartet (Exitcode " + process.ExitCode + "): " + instanceId);
                }
                catch (Exception ex)
                {
                    App.LogApp("DI-Stau: Neustart fehlgeschlagen (" + instanceId + "): " + ex.GetType().Name + ": " + ex.Message);
                }
            }

            PhysicalDeviceResolver.InvalidateDeviceCaches();
            // Windows braucht einen Moment, bis die neu gestarteten HID-Sammlungen
            // wieder antworten; ohne die Pause misst man den alten Stau nach.
            Thread.Sleep(1500);

            var watch = Stopwatch.StartNew();
            DirectInputControllerProvider.Enumerate(verboseLogging: false);
            watch.Stop();

            bool ok = watch.ElapsedMilliseconds < 3000;
            App.LogApp("DI-Stau: Aufzählung nach dem Neustart " + watch.ElapsedMilliseconds + " ms" + (ok ? " – behoben." : " – weiter langsam."));
            return ok
                ? (true, "DirectInput antwortet wieder (" + watch.ElapsedMilliseconds + " ms).")
                : (false, "DirectInput bleibt langsam (" + watch.ElapsedMilliseconds + " ms).");
        }

        /// <summary>
        /// Aktiviert die Schnittstelle und meldet, was daraus geworden ist.
        /// Der Text ist für das Banner im Hauptfenster gedacht.
        /// </summary>
        public static (bool Success, string Message) Enable(string? instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId))
                return (false, "Keine deaktivierte Schnittstelle bekannt.");

            string id = instanceId.Trim();
            try
            {
                var psi = new ProcessStartInfo("powershell.exe", BuildEnableArguments(id))
                {
                    UseShellExecute = true,
                    // Ohne Admin-Rechte fragt Windows einmalig nach; läuft die App bereits
                    // erhöht, wäre ein zweiter Prompt nur lästig.
                    Verb = InputSimulator.IsRunningAsAdmin() ? "" : "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                using var process = Process.Start(psi);
                if (process == null)
                    return (false, "Aktivieren konnte nicht gestartet werden.");

                process.WaitForExit(20000);
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // 1223 = ERROR_CANCELLED: der Nutzer hat die UAC-Abfrage abgelehnt.
                App.LogApp("Schnittstelle aktivieren: UAC-Abfrage abgebrochen (" + id + ")");
                return (false, "Abgebrochen – Administratorrechte nicht erteilt.");
            }
            catch (Exception ex)
            {
                App.LogApp("Schnittstelle aktivieren fehlgeschlagen (" + id + "): " + ex.GetType().Name + ": " + ex.Message);
                return (false, "Aktivieren fehlgeschlagen: " + ex.Message);
            }

            PhysicalDeviceResolver.InvalidateDeviceCaches();

            // Windows braucht einen Moment, bis der Treiber die Schnittstelle gestartet hat.
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(StartTimeoutMs);
            while (DateTime.UtcNow < deadline)
            {
                if (PhysicalDeviceResolver.IsInterfaceStarted(id)) break;
                Thread.Sleep(400);
            }

            if (PhysicalDeviceResolver.IsInterfaceStarted(id))
            {
                App.LogApp("Schnittstelle aktiviert: " + id);
                return (true, "Schnittstelle aktiviert – Windows führt das Gerät wieder.");
            }

            App.LogApp("Schnittstelle nach dem Aktivieren nicht gestartet: " + id);
            return (false, "Nach dem Aktivieren noch nicht sichtbar – bitte im Geräte-Manager prüfen.");
        }
    }
}
