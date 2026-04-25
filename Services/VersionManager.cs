using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

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

        // GitHub repository info - Update these!
        private const string GITHUB_OWNER = "your-username";  // Change this
        private const string GITHUB_REPO = "your-repo";       // Change this
        private const string GITHUB_API_URL = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";

        // Or use a custom endpoint if you prefer
        private const string UPDATE_CHECK_URL = "https://api.github.com/repos/YOUR_USERNAME/YOUR_REPO/releases/latest";

        public VersionManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WorkPulse-AutoUpdater");

            // Create directories if they don't exist
            if (!Directory.Exists(UpdateFolder))
                Directory.CreateDirectory(UpdateFolder);
            if (!Directory.Exists(BackupFolder))
                Directory.CreateDirectory(BackupFolder);
        }

        public Version GetCurrentVersion()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);

            // Try to get from version file first (for updated apps)
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

            // Fallback to assembly version
            return new Version(fileVersionInfo.FileVersion ?? "1.0.0");
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersion();
                Debug.WriteLine($"Current version: {currentVersion}");

                var response = await _httpClient.GetAsync(GITHUB_API_URL);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"GitHub API error: {response.StatusCode}");
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

                // Check if assets contain the zip file
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

                Debug.WriteLine($"Latest version: {latestVersion}, Has update: {hasUpdate}");

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
            // Remove 'v' prefix if present (e.g., "v1.0.5" -> "1.0.5")
            if (tag.StartsWith("v"))
                tag = tag.Substring(1);

            if (Version.TryParse(tag, out var version))
                return version;

            return new Version(1, 0, 0);
        }

        private bool IsMajorUpdate(Version current, Version latest)
        {
            // If major version changed or minor version increased by more than 1
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

                // Clean up old extraction folder
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                Directory.CreateDirectory(extractPath);

                // Download the zip file
                progress?.Report(10);
                Debug.WriteLine($"Downloading update from: {updateInfo.ReleaseUrl}");

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
                Debug.WriteLine($"Download complete: {zipPath}");

                // Extract the zip file
                await Task.Run(() => System.IO.Compression.ZipFile.ExtractToDirectory(zipPath, extractPath, true));
                progress?.Report(95);
                Debug.WriteLine($"Extraction complete: {extractPath}");

                // Create backup of current installation
                var currentExePath = Assembly.GetExecutingAssembly().Location;
                var appDirectory = Path.GetDirectoryName(currentExePath);
                var backupPath = Path.Combine(BackupFolder, $"backup_{DateTime.Now:yyyyMMdd_HHmmss}");

                await Task.Run(() => CopyDirectory(appDirectory, backupPath));
                progress?.Report(98);
                Debug.WriteLine($"Backup created: {backupPath}");

                // Replace files
                await Task.Run(() => ReplaceApplicationFiles(extractPath, appDirectory));
                progress?.Report(100);
                Debug.WriteLine($"Files replaced successfully");

                // Save the new version info
                SaveVersionInfo(updateInfo.Version);

                // Clean up
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
            // Skip the updater itself if it's running
            var currentExe = Assembly.GetExecutingAssembly().Location;
            var currentExeName = Path.GetFileName(currentExe);

            foreach (var sourceFile in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var fileName = Path.GetFileName(sourceFile);
                var targetFile = Path.Combine(targetDir, fileName);

                // Skip copying the currently running executable
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

            // Also copy subdirectories
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
                var dirName = Path.GetDirectoryName(directory);
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

                // Keep only last 3 backups
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