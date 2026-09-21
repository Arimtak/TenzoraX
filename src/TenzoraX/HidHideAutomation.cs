using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace TenzoraX
{
    /// <summary>
    /// Automatisiert das Verstecken des physischen Gamepads über HidHideCLI.exe,
    /// damit Spiele nur die virtuelle Xbox 360 sehen (kein Doppel-Input) – ohne
    /// dass der Nutzer den HidHide Configuration Client manuell einrichten muss.
    ///
    /// Wichtig (ehrlich): Die CLI braucht Administrator-Rechte. Läuft TenzoraX
    /// nicht bereits als Admin, fragt Windows einmalig per UAC nach.
    /// </summary>
    public static class HidHideAutomation
    {
        /// <summary>
        /// Sucht HidHideCLI.exe: Standard-Installationsordner (inkl. x64/x86-Unterordnern),
        /// Dokumente-Datenordner von TenzoraX und Registry-Deinstallations-Einträge.
        /// </summary>
        public static string? FindCli()
        {
            string docRoot = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var bases = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Nefarius Software Solutions", "HidHide"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Nefarius Software Solutions", "HidHide"),
                Path.Combine(docRoot, "TenzoraX"),
                Path.Combine(docRoot, "TenzoraX", "drivers")
            };
            foreach (var baseDir in bases)
            {
                var exe = FindCliInDir(baseDir);
                if (exe != null) return exe;
            }

            // Registry-Deinstallations-Einträge (InstallLocation), 64- und 32-Bit-Sicht
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(Microsoft.Win32.RegistryHive.LocalMachine, view);
                    using var uninstall = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstall == null) continue;

                    foreach (var subName in uninstall.GetSubKeyNames())
                    {
                        try
                        {
                            using var entry = uninstall.OpenSubKey(subName);
                            string? displayName = entry?.GetValue("DisplayName") as string;
                            if (string.IsNullOrEmpty(displayName) ||
                                displayName.IndexOf("HidHide", StringComparison.OrdinalIgnoreCase) < 0)
                                continue;

                            string? location = entry?.GetValue("InstallLocation") as string;
                            if (string.IsNullOrEmpty(location)) continue;
                            var exe = FindCliInDir(location);
                            if (exe != null) return exe;
                        }
                        catch { }
                    }
                }
                catch { }
            }
            return null;
        }

        private static string? FindCliInDir(string baseDir)
        {
            foreach (var sub in new[] { "", "x64", "x86" })
            {
                var p = string.IsNullOrEmpty(sub)
                    ? Path.Combine(baseDir, "HidHideCLI.exe")
                    : Path.Combine(baseDir, sub, "HidHideCLI.exe");
                if (File.Exists(p)) return p;
            }
            return null;
        }

        /// <summary>
        /// Führt einen einzelnen HidHideCLI-Befehl direkt aus – nur für den Fall, dass
        /// TenzoraX bereits als Administrator läuft. Ohne Admin-Rechte läuft alles über
        /// <see cref="RunCliBatch"/> in einem einzigen erhöhten Aufruf. Die CLI verarbeitet
        /// den Befehl sofort (per IOCTL) und bleibt nur wegen ihres interaktiven
        /// Konsolen-Modus offen; geschlossenes stdin (EOF) beendet sie nach dem Befehl
        /// selbst (verifiziert: --version/--cloak-state mit gepipetem stdin, Exit-Code 0).
        /// </summary>
        private static bool RunCli(string? cliPath, string arguments, int waitMs = 1500)
        {
            if (string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath)) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.StandardInput.Close(); // EOF → CLI beendet sich nach dem Befehl
                if (!p.WaitForExit(waitMs))
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Führt mehrere CLI-Befehle aus. Wichtig: Alle Befehle laufen in EINEM
        /// erhöhten cmd.exe-Aufruf (also ein einziger UAC-Dialog), denn ein Pad hat
        /// leicht 10 Schnittstellen – einzeln erhöht gestartet käme pro Schnittstelle
        /// eine eigene UAC-Abfrage. Läuft TenzoraX bereits als Admin, werden die
        /// Befehle direkt ausgeführt.
        ///
        /// Rückgabe = Erfolg des LETZTEN Befehls (das ist immer der kritische:
        /// --cloak-on bzw. --cloak-off). allOk meldet, ob zusätzlich alle Vorbereitungs-
        /// Befehle (-app-reg, -dev-hide) gelungen sind; einzelne Fehlschläge dort
        /// werden geloggt, brechen den Vorgang aber nicht ab.
        /// </summary>
        private static bool RunCliBatch(string? cliPath, IReadOnlyList<string> commands, out bool allOk, int waitMs = 1500)
        {
            allOk = false;
            if (string.IsNullOrEmpty(cliPath) || commands.Count == 0) return false;
            if (!File.Exists(cliPath)) return false;

            if (InputSimulator.IsRunningAsAdmin())
            {
                bool anyFailed = false, lastOk = false;
                foreach (var command in commands)
                {
                    lastOk = RunCli(cliPath, command, waitMs);
                    if (!lastOk)
                    {
                        anyFailed = true;
                        App.LogApp("HidHide: Befehl fehlgeschlagen: " + command);
                    }
                }
                allOk = !anyFailed;
                return lastOk;
            }

            // Ohne Admin: alle Befehle in einem erhöhten cmd-Aufruf verketten. Das
            // echo. liefert der CLI leeres stdin (sie beendet sich nach dem Befehl
            // selbst); der Exit-Code von cmd ist der des letzten Befehls.
            try
            {
                var chain = new System.Text.StringBuilder();
                foreach (var command in commands)
                {
                    if (chain.Length > 0) chain.Append(" & ");
                    chain.Append("echo. | \"").Append(cliPath).Append("\" ").Append(command);
                }

                var psi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"" + chain + "\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using var p = Process.Start(psi);
                if (p == null) return false;

                bool exited = p.WaitForExit(waitMs * 3 + commands.Count * 400);
                allOk = exited && p.ExitCode == 0;
                return allOk;
            }
            catch
            {
                // Nutzer hat den UAC-Dialog abgebrochen oder cmd ließ sich nicht starten.
                return false;
            }
        }

        /// <summary>
        /// Versteckt den physischen Controller vor allen Apps und erlaubt TenzoraX.exe,
        /// das Gerät weiter zu lesen. Reihenfolge wichtig: erst die App freigeben
        /// (--app-reg), dann die Geräte verstecken (--dev-hide) und Cloaking aktivieren.
        /// Vorher werden versehentlich hinterlassene leere Einträge (z.B. "   ") aus der
        /// versteckten Geräteliste entfernt – sie verursachen sonst unnötige UAC-Prompts
        /// und ein irreführendes Cloaking ohne sichtbaren Effekt.
        /// </summary>
        public static bool HideController(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            return HideController(new[] { instanceId });
        }

        /// <summary>
        /// Versteckt ALLE Schnittstellen eines physischen Controllers. Ein Pad hängt
        /// oft als Composite-Gerät mit mehreren Schnittstellen am Bus (XInput, HID,
        /// zweiter Composite-Zweig) – würde nur ein Pfad versteckt, bliebe der
        /// Controller über die übrigen Schnittstellen sichtbar (Doppel-Input).
        /// Die Aufzählung liefert <see cref="TenzoraX.Detection.PhysicalDeviceResolver"/>.
        /// Ergebnis: true, wenn das Cloaking aktiviert wurde (nur dann sind die Geräte
        /// für andere Programme wirklich unsichtbar); einzeln fehlgeschlagene Pfade
        /// werden protokolliert, brechen den Vorgang aber nicht ab.
        /// </summary>
        public static bool HideController(IReadOnlyCollection<string>? instanceIds)
        {
            var ids = Normalize(instanceIds);
            if (ids.Count == 0) return false;
            return ApplyDeviceState(ids, Array.Empty<string>(), cloakOn: true);
        }

        /// <summary>
        /// Stellt einen gewünschten Sichtbarkeits-Zustand in EINEM Arbeitsgang her:
        /// neue Geräte verstecken, nicht mehr benötigte freigeben und Cloaking an/aus.
        ///
        /// Diese Zustands-Abstimmung wird von der Spiegelung genutzt,
        /// damit sie sich nicht gegenseitig die Geräte wieder freigeben – wer gerade
        /// versteckt sein soll, ergibt sich aus den aktiven Optionen, nicht aus der
        /// Reihenfolge der Klicks.
        /// </summary>
        public static bool ApplyDeviceState(IReadOnlyCollection<string>? hide, IReadOnlyCollection<string>? unhide, bool cloakOn)
        {
            var hideIds = Normalize(hide);
            var unhideIds = Normalize(unhide);

            string? cli = FindCli();
            if (cli == null) return false;

            // Reihenfolge zählt: erst aufräumen, dann die App freigeben (nur nötig, wenn
            // etwas versteckt wird), dann Geräte verstecken/freigeben – und zuletzt
            // Cloaking (das ist der kritische Befehl).
            var commands = BuildCleanupCommands(cli);
            if (hideIds.Count > 0)
            {
                string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(appPath))
                    commands.Add("--app-reg \"" + appPath + "\"");
            }
            foreach (var id in hideIds)
                commands.Add("--dev-hide \"" + id + "\"");
            foreach (var id in unhideIds)
                commands.Add("--dev-unhide \"" + id + "\"");
            commands.Add(cloakOn ? "--cloak-on" : "--cloak-off");

            bool ok = RunCliBatch(cli, commands, out bool allOk);
            App.LogApp("HidHide: +" + hideIds.Count + " versteckt, -" + unhideIds.Count
                       + " freigegeben, Cloaking=" + cloakOn
                       + " (alle Befehle ok: " + allOk + ", Ergebnis: " + ok + ")");
            return ok;
        }

        /// <summary>Hebt das Verstecken wieder auf (Gerät(e) abwählen + Cloaking deaktivieren).</summary>
        public static bool Unhide(string instanceId)
        {
            return Unhide(string.IsNullOrWhiteSpace(instanceId) ? Array.Empty<string>() : new[] { instanceId });
        }

        /// <summary>
        /// Hebt das Verstecken für alle übergebenen Schnittstellen auf. Eine leere
        /// Liste ist zulässig und schaltet nur das Cloaking ab (z.B. um Reste
        /// früherer Läufe zu beseitigen).
        /// </summary>
        public static bool Unhide(IReadOnlyCollection<string>? instanceIds)
            => ApplyDeviceState(Array.Empty<string>(), instanceIds ?? Array.Empty<string>(), cloakOn: false);

        /// <summary>
        /// True, wenn wirklich ALLE übergebenen Schnittstellen in HidHide als versteckt
        /// eingetragen sind. Reine Leseoperation (ein CLI-Aufruf). Wird beim Start
        /// verwendet, damit ein halb verstecktes Gerät (nur eine von zwei
        /// Schnittstellen) NICHT als "schon erledigt" gilt.
        /// </summary>
        public static bool AreAllDevicesHidden(IReadOnlyCollection<string>? instanceIds)
        {
            var ids = Normalize(instanceIds);
            if (ids.Count == 0) return false;

            var hidden = ListHiddenDevices();
            foreach (var id in ids)
            {
                bool found = false;
                foreach (var entry in hidden)
                {
                    if (string.Equals(entry, id, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>Bereinigt Eingaben: trimmen, Leereinträge und Duplikate entfernen (Groß-/Kleinschreibung egal).</summary>
        private static List<string> Normalize(IReadOnlyCollection<string>? instanceIds)
        {
            var result = new List<string>();
            if (instanceIds == null) return result;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in instanceIds)
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                string id = raw.Trim();
                if (seen.Add(id)) result.Add(id);
                if (result.Count >= MaxInterfacesPerHide) break; // Schutz vor absurden Listen
            }
            return result;
        }

        /// <summary>Obergrenze pro Verstecken-Vorgang (ein Pad hat typisch 2–8 Schnittstellen).</summary>
        private const int MaxInterfacesPerHide = 16;

        /// <summary>
        /// Liest die aktuell in HidHide als versteckt eingetragenen Geräte-Instanzpfade
        /// (--dev-list). Reine Leseoperation, ohne Administrator-Rechte.
        /// </summary>
        public static List<string> ListHiddenDevices()
        {
            string? cli = FindCli();
            if (cli == null) return new List<string>();
            return ListHiddenDevices(cli);
        }

        private static List<string> ListHiddenDevices(string cli)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = "--dev-list",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return new List<string>();
                p.StandardInput.Close();
                if (!p.WaitForExit(2000))
                {
                    try { p.Kill(); } catch { }
                    return new List<string>();
                }
                return ParseDevList(p.StandardOutput.ReadToEnd());
            }
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>
        /// Parst die Ausgabe von "--dev-list" (Zeilen wie  --dev-hide "<instanzpfad>" )
        /// in eine Liste der Instanzpfade. Rein, ohne Seiteneffekte (testbar).
        /// </summary>
        internal static List<string> ParseDevList(string? output)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(output)) return result;
            foreach (var rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                int idx = line.IndexOf("--dev-hide", StringComparison.OrdinalIgnoreCase);
                if (idx < 0) continue;
                string rest = line.Substring(idx + "--dev-hide".Length).Trim();
                // Instanzpfad ist in Anführungszeichen: "<pfad>"
                if (rest.Length >= 2 && rest[0] == '"')
                {
                    int end = rest.IndexOf('"', 1);
                    if (end > 1)
                        result.Add(rest.Substring(1, end - 1));
                }
            }
            return result;
        }

        /// <summary>
        /// Liefert die Befehle, die ungültige (nur-Whitespace) Einträge aus HidHides
        /// versteckter Geräteliste entfernen. Solche Einträge entstehen z.B. durch
        /// fehlgeschlagene frühere Aufrufe mit leerem Instanzpfad und aktivieren Cloaking
        /// ohne jeden Effekt. Die Befehle laufen im selben Batch wie der eigentliche
        /// Vorgang – so kostet das Aufräumen keinen zusätzlichen UAC-Dialog.
        /// </summary>
        private static List<string> BuildCleanupCommands(string cli)
        {
            var commands = new List<string>();
            List<string> entries;
            try { entries = ListHiddenDevices(cli); }
            catch { return commands; }
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    commands.Add("--dev-unhide \"" + entry + "\"");
            }
            return commands;
        }

        /// <summary>
        /// Prüft, ob ein konkretes Gerät (Instanzpfad) aktuell in HidHide als
        /// versteckt eingetragen ist (reine Leseoperation, funktioniert ohne
        /// Administrator-Rechte). Wird beim Start verwendet: Nur wenn das echte
        /// physische Gamepad fehlt, wird (wieder) versteckt – ein bereits aktives
        /// Cloaking ohne dieses Gerät (z.B. Reste früherer Zustände) führt sonst
        /// dazu, dass der Controller sichtbar bleibt.
        /// </summary>
        public static bool IsDeviceHidden(string instanceId)
        {
            if (string.IsNullOrWhiteSpace(instanceId)) return false;
            foreach (var entry in ListHiddenDevices())
                if (string.Equals(entry, instanceId, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>
        /// Liest den aktuellen Cloaking-Status von HidHide (reine Leseoperation, ohne
        /// Administrator-Rechte). Für die Diagnose: Nur wenn Cloaking AKTIV ist UND das
        /// Gerät in der versteckten Liste steht, ist das physische Gamepad für andere
        /// Programme tatsächlich unsichtbar.
        /// </summary>
        public static bool IsCloakActive()
        {
            string? cli = FindCli();
            if (cli == null) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = "--cloak-state",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var p = Process.Start(psi);
                if (p == null) return false;
                p.StandardInput.Close();
                if (!p.WaitForExit(2000))
                {
                    try { p.Kill(); } catch { }
                    return false;
                }
                string output = p.StandardOutput.ReadToEnd().Trim();
                return output.IndexOf("--cloak-on", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }
    }
}
