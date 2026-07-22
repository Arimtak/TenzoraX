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

        public static async Task<string?> DownloadUpdate(string url)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "TenzoraXUpdate");
                Directory.CreateDirectory(tempDir);
                string zipPath = Path.Combine(tempDir, "update.zip");

                var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd("TenzoraX-Updater/1.0");
                using var resp = await _http.SendAsync(req);
                if (!resp.IsSuccessStatusCode) return null;

                using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await resp.Content.CopyToAsync(fs);

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
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string currentExe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            string currentExeName = Path.GetFileName(currentExe) + ".exe";
            if (!currentExe.EndsWith(".exe")) currentExeName = Path.GetFileName(currentExe);

            string oldExe = Path.Combine(appDir, Path.GetFileNameWithoutExtension(currentExe) + ".old.exe");
            string targetExe = Path.Combine(appDir, Path.GetFileName(currentExe));

            try
            {
                if (File.Exists(oldExe)) File.Delete(oldExe);
                File.Move(targetExe, oldExe);
                File.Move(newExePath, targetExe);
            }
            catch
            {
                // Restore on failure
                try { File.Move(oldExe, targetExe); } catch { }
                throw;
            }

            Process.Start(targetExe);
            Environment.Exit(0);
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
