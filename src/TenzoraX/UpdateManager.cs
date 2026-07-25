using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace TenzoraX
{
    public class UpdateInfo
    {
        public string LatestVersion { get; set; } = "";
        public string DownloadUrl { get; set; } = "";
        public string ReleaseUrl { get; set; } = "";
    }

    public class UpdateManager
    {
        private static readonly HttpClient _http = new();
        private const string RepoApi = "https://api.github.com/repos/Arimtak/TenzoraX/releases/latest";

        public static async Task<UpdateInfo?> CheckForUpdate()
        {
            try
            {
                var req = new HttpRequestMessage(HttpMethod.Get, RepoApi);
                req.Headers.UserAgent.ParseAdd("TenzoraX-Updater/1.0");
                var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
                var root = doc.RootElement;
                string tag = root.GetProperty("tag_name").GetString() ?? "";
                if (!tag.StartsWith("v")) return null;
                string latest = tag.Substring(1);

                if (CompareVersions(latest, AppVersion.Current) <= 0)
                    return null;

                string downloadUrl = "";
                string releaseUrl = root.GetProperty("html_url").GetString() ?? "";

                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        string name = asset.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl)) return null;

                return new UpdateInfo
                {
                    LatestVersion = latest,
                    DownloadUrl = downloadUrl,
                    ReleaseUrl = releaseUrl
                };
            }
            catch (Exception ex) { App.LogApp($"CheckForUpdate-Fehler: {ex.GetType().Name}: {ex.Message}"); return null; }
        }

        public static async Task<string?> DownloadUpdate(string url, IProgress<int>? progress = null)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TenzoraXUpdate");
                Directory.CreateDirectory(tempDir);
                bool isZip = url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
                string downloadPath = Path.Combine(tempDir, isZip ? "update.zip" : "update.exe");

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("TenzoraX-Updater/1.0");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return null;

                long totalBytes = resp.Content.Headers.ContentLength ?? -1;
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(downloadPath, FileMode.Create, FileAccess.Write, FileShare.None);

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

                if (isZip)
                {
                    string extractDir = Path.Combine(tempDir, "extracted");
                    if (Directory.Exists(extractDir))
                        Directory.Delete(extractDir, true);
                    ZipFile.ExtractToDirectory(downloadPath, extractDir);

                    var exeFiles = Directory.GetFiles(extractDir, "*.exe", SearchOption.TopDirectoryOnly);
                    if (exeFiles.Length == 0) return null;
                    return exeFiles[0];
                }

                return downloadPath;
            }
            catch (Exception ex) { App.LogApp($"DownloadUpdate-Fehler: {ex.GetType().Name}: {ex.Message}"); return null; }
        }

        public static void InstallUpdate(string newExePath)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe)) return;

            int pid = Process.GetCurrentProcess().Id;
            string tempDir = Path.Combine(Path.GetTempPath(), "TenzoraXUpdate");
            Directory.CreateDirectory(tempDir);
            string scriptPath = Path.Combine(tempDir, "update.ps1");
            string logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "TenzoraX", "Logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, "update.log");
            string targetDir = Path.GetDirectoryName(currentExe) ?? "";

            App.LogApp($"InstallUpdate: currentExe={currentExe}");
            App.LogApp($"InstallUpdate: newExePath={newExePath}");
            App.LogApp($"InstallUpdate: pid={pid}");

            string script = $@"param()
try {{
    $log = ""{logPath}""
    $old = ""{currentExe}""
    $new = ""{newExePath}""
    $targetPid = {pid}
    $now = Get-Date -Format 'HH:mm:ss'
    Add-Content -Path $log -Value ""[$now] Update gestartet""
    Add-Content -Path $log -Value ""[$now] alt: $old""
    Add-Content -Path $log -Value ""[$now] neu: $new""
    Add-Content -Path $log -Value ""[$now] PID: $targetPid""

    Add-Content -Path $log -Value ""[$now] Warte auf Prozessende ...""
    while ((Get-Process -Id $targetPid -ErrorAction SilentlyContinue) -ne $null) {{
        Start-Sleep -Milliseconds 500
    }}
    Start-Sleep -Seconds 1
    $now = Get-Date -Format 'HH:mm:ss'
    Add-Content -Path $log -Value ""[$now] Prozess beendet""

    Add-Content -Path $log -Value ""[$now] Lösche: $old""
    Remove-Item -LiteralPath $old -Force -ErrorAction Stop
    Add-Content -Path $log -Value ""[$now] Alt gelöscht""

    Add-Content -Path $log -Value ""[$now] Kopiere: $new -> $old""
    Copy-Item -LiteralPath $new -Destination $old -Force -ErrorAction Stop
    Add-Content -Path $log -Value ""[$now] Kopiert""

    if (Test-Path $old) {{
        Add-Content -Path $log -Value ""[$now] Neue EXE existiert, starte ...""
        Start-Process -FilePath $old
        Add-Content -Path $log -Value ""[$now] Neue Version gestartet""
    }} else {{
        Add-Content -Path $log -Value ""[$now] FEHLER: Neue EXE nicht gefunden!""
    }}

    Remove-Item -LiteralPath ""$env:TEMP\TenzoraXUpdate"" -Recurse -Force -ErrorAction SilentlyContinue
    $now = Get-Date -Format 'HH:mm:ss'
    Add-Content -Path $log -Value ""[$now] Update abgeschlossen""
}} catch {{
    $err = $_.Exception.Message
    $now = Get-Date -Format 'HH:mm:ss'
    Add-Content -Path $log -Value ""[$now] FEHLER: $err""
}}
";
            File.WriteAllText(scriptPath, script);
            App.LogApp($"Update-Skript erstellt: {scriptPath}");

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -NoProfile -File \"{scriptPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
            App.LogApp("Updater-Prozess gestartet – TenzoraX wird beendet");
            Environment.Exit(0);
        }

        public static void CleanupTemp()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TenzoraXUpdate");
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch { }
        }

        private static int CompareVersions(string a, string b)
        {
            var va = a.Split('.');
            var vb = b.Split('.');
            int max = Math.Max(va.Length, vb.Length);
            for (int i = 0; i < max; i++)
            {
                int na = i < va.Length && int.TryParse(va[i], out var x) ? x : 0;
                int nb = i < vb.Length && int.TryParse(vb[i], out var y) ? y : 0;
                if (na != nb) return na.CompareTo(nb);
            }
            return 0;
        }
    }
}
