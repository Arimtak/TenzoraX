using System;
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
        /// Führt einen HidHideCLI-Befehl aus. Die CLI verarbeitet den Befehl sofort
        /// (per IOCTL) und bleibt nur wegen ihres interaktiven Konsolen-Modus offen.
        /// Wird stdin geschlossen (EOF), beendet sie sich nach dem Befehl selbst
        /// (verifiziert: --version und --cloak-state mit gepipetem stdin, Exit-Code 0).
        ///
        /// Läuft TenzoraX als Admin, wird die CLI direkt mit geschlossenem stdin
        /// gestartet (kein Fenster, sauberes Ende). Läuft TenzoraX NICHT als Admin,
        /// wird sie einmalig erhöht gestartet (ein UAC-Prompt pro Umschaltvorgang)
        /// über den Wrapper "cmd /c echo. | <cli> <args>": Das echo. liefert leeres
        /// stdin, die CLI führt aus und beendet sich selbst, cmd ebenfalls – es
        /// entstehen keine verwaisten erhöhten Prozesse. Bricht der Nutzer die
        /// UAC-Abfrage ab, wirft Process.Start eine Win32Exception → false.
        /// </summary>
        private static bool RunCli(string? cliPath, string arguments, int waitMs = 1500)
        {
            if (string.IsNullOrEmpty(cliPath) || !File.Exists(cliPath)) return false;
            try
            {
                if (InputSimulator.IsRunningAsAdmin())
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

                // Nicht-Admin: einmalig erhöht starten (UAC-Prompt).
                var psi2 = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c \"echo. | \"" + cliPath + "\" " + arguments + "\"",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var p2 = Process.Start(psi2);
                if (p2 == null) return false;
                return p2.WaitForExit(waitMs * 3);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Versteckt den physischen Controller vor allen Apps und erlaubt TenzoraX.exe,
        /// das Gerät weiter zu lesen. Reihenfolge wichtig: erst die App freigeben
        /// (--app-reg), dann das Gerät verstecken (--dev-hide) und Cloaking aktivieren.
        /// Vorher werden versehentlich hinterlassene leere Einträge (z.B. "   ") aus der
        /// versteckten Geräteliste entfernt – sie verursachen sonst unnötige UAC-Prompts
        /// und ein irreführendes Cloaking ohne sichtbaren Effekt.
        /// </summary>
        public static bool HideController(string instanceId)
        {
            string? cli = FindCli();
            if (cli == null || string.IsNullOrWhiteSpace(instanceId)) return false;

            CleanInvalidHiddenEntries(cli);

            string appPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            bool ok = true;
            if (!string.IsNullOrEmpty(appPath))
                ok &= RunCli(cli, "--app-reg \"" + appPath + "\"");
            ok &= RunCli(cli, "--dev-hide \"" + instanceId + "\"");
            ok &= RunCli(cli, "--cloak-on");
            return ok;
        }

        /// <summary>Hebt das Verstecken wieder auf (Gerät abwählen + Cloaking deaktivieren).</summary>
        public static bool Unhide(string instanceId)
        {
            string? cli = FindCli();
            if (cli == null) return false;

            CleanInvalidHiddenEntries(cli);

            bool ok = true;
            if (!string.IsNullOrEmpty(instanceId))
                ok &= RunCli(cli, "--dev-unhide \"" + instanceId + "\"");
            ok &= RunCli(cli, "--cloak-off");
            return ok;
        }

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
        /// Entfernt ungültige (nur-Whitespace) Einträge aus der versteckten Geräteliste
        /// von HidHide. Solche Einträge entstehen z.B. durch fehlgeschlagene frühere
        /// Aufrufe mit leerem Instanzpfad und aktivieren Cloaking ohne jeden Effekt.
        /// </summary>
        private static void CleanInvalidHiddenEntries(string cli)
        {
            List<string> entries;
            try { entries = ListHiddenDevices(cli); }
            catch { return; }
            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    RunCli(cli, "--dev-unhide \"" + entry + "\"");
            }
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
