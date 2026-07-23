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
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
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
            catch { return null; }
        }

        public static async Task<string?> DownloadUpdate(string url, IProgress<int>? progress = null)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TenzoraXUpdate");
                Directory.CreateDirectory(tempDir);
                string zipPath = Path.Combine(tempDir, "update.zip");

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("TenzoraX-Updater/1.0");

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                if (!resp.IsSuccessStatusCode) return null;

                long totalBytes = resp.Content.Headers.ContentLength ?? -1;
                using var stream = await resp.Content.ReadAsStreamAsync();
                using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);

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

                string extractDir = Path.Combine(tempDir, "extracted");
                if (Directory.Exists(extractDir))
                    Directory.Delete(extractDir, true);
                ZipFile.ExtractToDirectory(zipPath, extractDir);

                var exeFiles = Directory.GetFiles(extractDir, "*.exe", SearchOption.TopDirectoryOnly);
                if (exeFiles.Length == 0) return null;

                return exeFiles[0];
            }
            catch { return null; }
        }

        public static void InstallUpdate(string newExePath)
        {
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            if (string.IsNullOrEmpty(currentExe)) return;

            int pid = Process.GetCurrentProcess().Id;
            string batchDir = Path.Combine(Path.GetTempPath(), "TenzoraXUpdate");
            Directory.CreateDirectory(batchDir);
            string batchPath = Path.Combine(batchDir, "update.bat");
            string targetDir = Path.GetDirectoryName(currentExe) ?? "";

            string batchContent = $@"@echo off
setlocal
set ""target={currentExe}""
set ""newExe={newExePath}""
set ""pid={pid}""
set ""dir={targetDir}""

:wait
%SystemRoot%\System32\tasklist.exe /FI ""PID eq %pid%"" 2>nul | %SystemRoot%\System32\findstr.exe /I ""%pid%"" >nul
if not errorlevel 1 (
    %SystemRoot%\System32\timeout.exe /t 1 /nobreak >nul
    goto wait
)

%SystemRoot%\System32\timeout.exe /t 1 /nobreak >nul

:: Delete any leftover .old.exe from old update code
%SystemRoot%\System32\del.exe /F /Q ""%dir%\TenzoraX.old.exe"" >nul 2>&1

%SystemRoot%\System32\del.exe /F /Q ""%target%"" >nul 2>&1
%SystemRoot%\System32\copy.exe /Y ""%newExe%"" ""%target%"" >nul 2>&1

start """" /D ""{targetDir}"" ""%target%""

%SystemRoot%\System32\del.exe /F /Q ""%~f0"" >nul 2>&1
";

            File.WriteAllText(batchPath, batchContent);

            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c \"{batchPath}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };

            Process.Start(psi);
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
