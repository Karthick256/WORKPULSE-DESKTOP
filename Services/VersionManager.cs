using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace monitor_desktop.Services
{
    public class VersionInfo
    {
        public string Version { get; set; }
        public string ReleaseUrl { get; set; }
        public string ReleaseNotes { get; set; }
        public DateTime ReleaseDate { get; set; }
        public bool IsMandatory { get; set; }
    }

    public class UpdateInfo
    {
        public bool HasUpdate { get; set; }
        public VersionInfo LatestVersion { get; set; }
        public VersionInfo CurrentVersion { get; set; }
    }

    public class VersionManager
    {
        private static readonly string UpdateFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkPulse",
            "updates");

        private static readonly string BackupFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkPulse",
            "backup");

        private static readonly string VersionFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkPulse",
            "version.json");

        private readonly HttpClient _httpClient;

        private const string GITHUB_OWNER = "Karthick256";
        private const string GITHUB_REPO = "WORKPULSE-DESKTOP";
        private const string GITHUB_API_URL = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";

        public VersionManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WorkPulse-AutoUpdater");

            if (!Directory.Exists(UpdateFolder))
                Directory.CreateDirectory(UpdateFolder);
            if (!Directory.Exists(BackupFolder))
                Directory.CreateDirectory(BackupFolder);
        }

        public Version GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

            if (File.Exists(VersionFilePath))
            {
                try
                {
                    var json = File.ReadAllText(VersionFilePath);
                    var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json);
                    if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Version))
                    {
                        return Version.Parse(versionInfo.Version);
                    }
                }
                catch { }
            }

            return new Version(fileVersionInfo.FileVersion ?? "1.0.0");
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersion();
                var response = await _httpClient.GetAsync(GITHUB_API_URL);

                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateInfo { HasUpdate = false };
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestTag = root.GetProperty("tag_name").GetString();
                var latestVersion = ParseVersionFromTag(latestTag);
                var releaseUrl = root.GetProperty("html_url").GetString();
                var releaseNotes = root.GetProperty("body").GetString();
                var releaseDate = root.GetProperty("published_at").GetDateTime();

                var assets = root.GetProperty("assets");
                string downloadUrl = null;
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString();
                    if (name != null && name.EndsWith(".zip"))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString();
                        break;
                    }
                }

                var hasUpdate = latestVersion > currentVersion;

                return new UpdateInfo
                {
                    HasUpdate = hasUpdate,
                    CurrentVersion = new VersionInfo
                    {
                        Version = currentVersion.ToString(),
                        ReleaseUrl = null,
                        ReleaseNotes = null,
                        ReleaseDate = DateTime.Now,
                        IsMandatory = false
                    },
                    LatestVersion = new VersionInfo
                    {
                        Version = latestVersion.ToString(),
                        ReleaseUrl = downloadUrl,
                        ReleaseNotes = releaseNotes,
                        ReleaseDate = releaseDate,
                        IsMandatory = IsMajorUpdate(currentVersion, latestVersion)
                    }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
                return new UpdateInfo { HasUpdate = false };
            }
        }

        private Version ParseVersionFromTag(string tag)
        {
            if (tag.StartsWith("v"))
                tag = tag.Substring(1);

            if (Version.TryParse(tag, out var version))
                return version;

            return new Version(1, 0, 0);
        }

        private bool IsMajorUpdate(Version current, Version latest)
        {
            return current.Major < latest.Major ||
                   (current.Major == latest.Major && latest.Minor - current.Minor >= 2);
        }

        public async Task<bool> DownloadAndInstallUpdateAsync(VersionInfo updateInfo, IProgress<int> progress = null)
        {
            try
            {
                if (string.IsNullOrEmpty(updateInfo?.ReleaseUrl))
                    return false;

                var zipPath = Path.Combine(UpdateFolder, $"update_{updateInfo.Version}.zip");
                var extractPath = Path.Combine(UpdateFolder, $"extracted_{updateInfo.Version}");
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                Directory.CreateDirectory(extractPath);
                progress?.Report(10);

                using (var response = await _httpClient.GetAsync(updateInfo.ReleaseUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    var totalBytes = response.Content.Headers.ContentLength ?? -1;

                    using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                    using (var httpStream = await response.Content.ReadAsStreamAsync())
                    {
                        var buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await httpStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes > 0)
                            {
                                var percent = (int)((totalRead * 80) / totalBytes) + 10;
                                progress?.Report(Math.Min(percent, 90));
                            }
                        }
                    }
                }

                progress?.Report(90);
                await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath, true));
                progress?.Report(95);

                var currentExePath = Assembly.GetExecutingAssembly().Location;
                var appDirectory = Path.GetDirectoryName(currentExePath);
                var backupPath = Path.Combine(BackupFolder, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}");

                await Task.Run(() => CopyDirectory(appDirectory, backupPath));
                progress?.Report(98);
                await Task.Run(() => ReplaceApplicationFiles(extractPath, appDirectory));
                progress?.Report(100);

                SaveVersionInfo(updateInfo.Version);
                File.Delete(zipPath);
                Directory.Delete(extractPath, true);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during update: {ex.Message}");
                return false;
            }
        }

        private void ReplaceApplicationFiles(string sourceDir, string targetDir)
        {
            var currentExe = Assembly.GetExecutingAssembly().Location;
            var currentExeName = Path.GetFileName(currentExe);

            foreach (var sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(sourceFile);
                var targetFile = Path.Combine(targetDir, fileName);

                if (fileName.Equals(currentExeName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    File.Copy(sourceFile, targetFile, true);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to copy {fileName}: {ex.Message}");
                }
            }

            foreach (var sourceSubDir in Directory.GetDirectories(sourceDir))
            {
                var dirName = Path.GetFileName(sourceSubDir);
                var targetSubDir = Path.Combine(targetDir, dirName);

                if (!Directory.Exists(targetSubDir))
                    Directory.CreateDirectory(targetSubDir);

                CopyDirectory(sourceSubDir, targetSubDir);
            }
        }

        private void CopyDirectory(string sourceDir, string targetDir)
        {
            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                var fileName = Path.GetFileName(file);
                var targetFile = Path.Combine(targetDir, fileName);
                File.Copy(file, targetFile, true);
            }

            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                var targetSubDir = Path.Combine(targetDir, Path.GetFileName(directory));
                CopyDirectory(directory, targetSubDir);
            }
        }

        private void SaveVersionInfo(string version)
        {
            var versionInfo = new VersionInfo
            {
                Version = version,
                ReleaseDate = DateTime.Now
            };

            var json = JsonSerializer.Serialize(versionInfo);
            File.WriteAllText(VersionFilePath, json);
        }

        public void CleanupOldUpdates()
        {
            try
            {
                var updateFiles = Directory.GetFiles(UpdateFolder, "update_*.zip");
                foreach (var file in updateFiles)
                {
                    File.Delete(file);
                }

                var extractDirs = Directory.GetDirectories(UpdateFolder, "extracted_*");
                foreach (var dir in extractDirs)
                {
                    Directory.Delete(dir, true);
                }

                var backups = Directory.GetDirectories(BackupFolder, "backup_*")
                    .OrderByDescending(d => d)
                    .Skip(3);
                foreach (var backup in backups)
                {
                    Directory.Delete(backup, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }

        public bool RestoreFromBackup()
        {
            try
            {
                var latestBackup = Directory.GetDirectories(BackupFolder, "backup_*")
                    .OrderByDescending(d => d)
                    .FirstOrDefault();

                if (latestBackup == null)
                    return false;

                var currentExePath = Assembly.GetExecutingAssembly().Location;
                var appDirectory = Path.GetDirectoryName(currentExePath);
                CopyDirectory(latestBackup, appDirectory);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Restore error: {ex.Message}");
                return false;
            }
        }
    }
}