using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TenzoraX
{
    /// <summary>
    /// Hilft, die lokal hinterlegten Kernel-Treiber-Installer (ViGEmBus, HidHide) zu finden
    /// und zu starten. Die Installer liegen als offizielle Setup-EXE-Dateien im Unterordner
    /// „drivers\\“ neben der TenzoraX.exe.
    ///
    /// Wichtig (ehrlich): Eine App kann Kernel-Treiber NICHT „stumm“ im Hintergrund
    /// installieren. Windows verlangt dafür zwingend eine einmalige Administrator-Bestätigung
    /// (UAC). Diese Klasse macht es dem Nutzer aber so einfach wie möglich: Er klickt in
    /// TenzoraX auf „Installieren“, die App findet die fertig heruntergeladene Setup-Datei,
    /// ruft sie mit „Als Administrator ausführen“ auf und startet damit den offiziellen
    /// Windows-Installer – es bleibt nur noch der einmalige UAC-/Installer-Klick.
    /// </summary>
    public static class DriverInstaller
    {
        public const string ViGEmBusFile = "ViGEmBusSetup.exe";
        public const string HidHideFile = "HidHideSetup.exe";

        /// <summary>Offizielle Download-URLs (Nefarius GitHub Releases). Die Dateien werden
        /// beim ersten Bedarf nach „Dokumente\TenzoraX\drivers“ geladen und von dort installiert.</summary>
        public const string ViGEmBusDownloadUrl =
            "https://github.com/nefarius/ViGEmBus/releases/download/v1.22.0/ViGEmBus_1.22.0_x64_x86_arm64.exe";
        public const string HidHideDownloadUrl =
            "https://github.com/nefarius/HidHide/releases/download/v1.5.230.0/HidHide_1.5.230_x64.exe";

        private static readonly HttpClient _http = new();

        /// <summary>Der dauerhafte Treiber-Ordner: „Dokumente\TenzoraX\drivers“.
        /// Die Setup-Dateien liegen dort bei jedem Nutzer – unabhängig davon, wo die EXE liegt.
        /// (GitHub-Downloads enthalten bewusst nur die TenzoraX.exe, keine Treiber.)</summary>
        public static string DriverFolder()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TenzoraX", "drivers");
            try { Directory.CreateDirectory(folder); } catch { }
            return folder;
        }

        /// <summary>Liefert die passende offizielle Download-URL zur Setup-Datei.</summary>
        public static string DownloadUrlFor(string fileName)
        {
            return fileName.Equals(ViGEmBusFile, StringComparison.OrdinalIgnoreCase)
                ? ViGEmBusDownloadUrl
                : HidHideDownloadUrl;
        }

        /// <summary>Stellt sicher, dass die Setup-Datei im Treiber-Ordner liegt.
        /// Fehlt sie, wird sie von der offiziellen Quelle heruntergeladen.
        /// Rückgabe: Pfad zur Datei oder null bei Fehler.</summary>
        public static async Task<string?> EnsureDownloadedAsync(string fileName, IProgress<int>? progress = null)
        {
            // Schon vorhanden? Dann nichts tun.
            var existing = Find(fileName);
            if (existing != null) return existing;

            try
            {
                string targetPath = Path.Combine(DriverFolder(), fileName);
                string url = DownloadUrlFor(fileName);

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("TenzoraX-Updater/1.0");
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return null;

                long totalBytes = resp.Content.Headers.ContentLength ?? -1;
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);

                byte[] buffer = new byte[8192];
                long totalRead = 0;
                int bytesRead;
                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await fs.WriteAsync(buffer, 0, bytesRead);
                    totalRead += bytesRead;
                    if (progress != null && totalBytes > 0)
                        progress.Report((int)(totalRead * 100 / totalBytes));
                }
                fs.Close();

                if (!File.Exists(targetPath) || new FileInfo(targetPath).Length == 0)
                {
                    try { File.Delete(targetPath); } catch { }
                    return null;
                }
                return targetPath;
            }
            catch (Exception ex)
            {
                App.LogApp($"Treiber-Download fehlgeschlagen ({fileName}): {ex.Message}");
                return null;
            }
        }

        /// <summary>Liefert den Pfad zur Setup-Datei, falls sie lokal vorhanden ist (sonst null).
        /// Gesucht wird zuerst im Dokumente-Datenordner („Dokumente\TenzoraX\drivers“ – dort
        /// liegen die Treiber bei jedem Nutzer dauerhaft), dann im driver-Unterordner neben
        /// der EXE und zuletzt direkt neben der EXE.</summary>
        public static string? Find(string fileName)
        {
            // 1) Dokumente-Datenordner: Dokumente\TenzoraX\drivers\ (primärer Speicherort)
            try
            {
                var doc = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "TenzoraX", "drivers", fileName);
                if (File.Exists(doc)) return doc;
            }
            catch { }

            // 2) neben der laufenden EXE im driver-Unterordner
            try
            {
                string baseDir = AppContext.BaseDirectory;
                var local = Path.Combine(baseDir, "drivers", fileName);
                if (File.Exists(local)) return local;
            }
            catch { }

            // 3) neben der laufenden EXE direkt
            try
            {
                var flat = Path.Combine(AppContext.BaseDirectory, fileName);
                if (File.Exists(flat)) return flat;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Startet die Setup-Datei mit „Als Administrator ausführen“. Liefert true, wenn der
        /// Start ausgelöst wurde. Der tatsächliche Erfolg der Installation hängt von der
        /// einmaligen Windows-UAC-Bestätigung ab (ist technisch nicht umgehbar).
        /// </summary>
        public static bool LaunchInstaller(string? setupPath)
        {
            if (string.IsNullOrEmpty(setupPath) || !File.Exists(setupPath)) return false;
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = setupPath,
                    UseShellExecute = true,
                    Verb = "runas"   // startet asynchron mit UAC-Prompt
                };
                Process.Start(psi);
                return true;
            }
            catch
            {
                // UAC abgebrochen / nicht möglich
                return false;
            }
        }

        /// <summary>Prüft, ob der jeweilige Treiber bereits installiert ist (Registry-Dienst).</summary>
        public static bool IsDriverInstalled(string serviceName)
        {
            try
            {
                using var root = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
                if (root == null) return false;
                foreach (var name in root.GetSubKeyNames())
                    if (name.Equals(serviceName, StringComparison.OrdinalIgnoreCase)) return true;
                return false;
            }
            catch { return false; }
        }
    }
}