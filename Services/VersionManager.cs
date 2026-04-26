using System.Diagnostics;
using System.IO;
using System.IO.Compression;
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
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

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

            var version = new Version(fileVersionInfo.FileVersion ?? "1.0.0");
            SaveVersionInfo(version.ToString());
            return version;
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

                var tagNameElement = root.GetProperty("tag_name");
                var latestTag = tagNameElement.GetString();

                if (string.IsNullOrEmpty(latestTag))
                {
                    return new UpdateInfo { HasUpdate = false };
                }

                var latestVersion = ParseVersionFromTag(latestTag);

                string releaseUrl = null;
                if (root.TryGetProperty("html_url", out var htmlUrlElement))
                {
                    releaseUrl = htmlUrlElement.GetString();
                }
                string releaseNotes = null;
                if (root.TryGetProperty("body", out var bodyElement))
                {
                    releaseNotes = bodyElement.GetString();
                }
                DateTime releaseDate = DateTime.Now;
                if (root.TryGetProperty("published_at", out var publishedAtElement))
                {
                    if (DateTime.TryParse(publishedAtElement.GetString(), out var parsedDate))
                    {
                        releaseDate = parsedDate;
                    }
                }
                string downloadUrl = null;
                if (root.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        if (asset.TryGetProperty("name", out var nameElement))
                        {
                            var name = nameElement.GetString();
                            if (name != null && name.EndsWith(".zip"))
                            {
                                if (asset.TryGetProperty("browser_download_url", out var downloadUrlElement))
                                {
                                    downloadUrl = downloadUrlElement.GetString();
                                    break;
                                }
                            }
                        }
                    }
                }
                if (string.IsNullOrEmpty(downloadUrl) && !string.IsNullOrEmpty(releaseUrl))
                {
                    downloadUrl = $"{releaseUrl}/download/{latestTag}/WorkPulse-{latestTag}.zip";
                }

                var hasUpdate = latestVersion > currentVersion;
                var updateInfo = new UpdateInfo
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
                    LatestVersion = hasUpdate ? new VersionInfo
                    {
                        Version = latestVersion.ToString(),
                        ReleaseUrl = downloadUrl ?? releaseUrl,
                        ReleaseNotes = releaseNotes ?? "No release notes available.",
                        ReleaseDate = releaseDate,
                        IsMandatory = IsMajorUpdate(currentVersion, latestVersion)
                    } : null
                };

                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                return new UpdateInfo { HasUpdate = false };
            }
            catch (JsonException ex)
            {
                return new UpdateInfo { HasUpdate = false };
            }
            catch (Exception ex)
            {
                return new UpdateInfo { HasUpdate = false };
            }
        }

        private Version ParseVersionFromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return new Version(1, 0, 0);

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
                {
                    Debug.WriteLine("No download URL available");
                    return false;
                }

                progress?.Report(5);

                var zipPath = Path.Combine(UpdateFolder, $"update_{updateInfo.Version.Replace('.', '_')}.zip");
                var extractPath = Path.Combine(UpdateFolder, $"extracted_{updateInfo.Version.Replace('.', '_')}");

                // Clean up old directories
                if (Directory.Exists(extractPath))
                    Directory.Delete(extractPath, true);
                Directory.CreateDirectory(extractPath);

                progress?.Report(10);

                // Download the update
                Debug.WriteLine($"Downloading from: {updateInfo.ReleaseUrl}");

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
                                var percent = (int)((totalRead * 70) / totalBytes) + 10;
                                progress?.Report(Math.Min(percent, 80));
                            }
                        }
                    }
                }

                progress?.Report(85);

                // Extract the zip file
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, extractPath, true));

                progress?.Report(90);

                // Create update script instead of replacing files directly
                var scriptPath = CreateUpdateScript(extractPath, updateInfo.Version);

                progress?.Report(95);

                // Launch the update script and exit
                var startInfo = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };

                Process.Start(startInfo);

                progress?.Report(100);

                // Save the new version info
                SaveVersionInfo(updateInfo.Version);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during update: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        private string CreateUpdateScript(string extractPath, string newVersion)
        {
            var currentExePath = Assembly.GetExecutingAssembly().Location;
            var currentExeName = Path.GetFileName(currentExePath);
            var currentDirectory = Path.GetDirectoryName(currentExePath);

            var scriptPath = Path.Combine(Path.GetTempPath(), $"workpulse_update_{DateTime.Now:yyyyMMddHHmmss}.bat");

            var scriptContent = $@"
@echo off
title WorkPulse Updater
echo ========================================
echo WorkPulse Updater
echo ========================================
echo.
echo Updating from version {GetCurrentVersion()} to {newVersion}...
echo.

timeout /t 2 /nobreak > nul

echo Copying new files...
xcopy ""{extractPath}\*"" ""{currentDirectory}\"" /E /Y /I /Q

echo.
echo Cleaning up...
rmdir /s /q ""{extractPath}"" 2>nul
del ""{extractPath.Replace("extracted", "update")}.zip"" 2>nul

echo.
echo Update complete! Starting WorkPulse...
timeout /t 2 /nobreak > nul

start """" ""{currentExePath}""

echo Exiting updater...
timeout /t 1 /nobreak > nul
del ""%~f0""
";

            File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.UTF8);
            return scriptPath;
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
                    var fileInfo = new FileInfo(file);
                    if (fileInfo.CreationTime < DateTime.Now.AddDays(-7))
                        File.Delete(file);
                }

                var extractDirs = Directory.GetDirectories(UpdateFolder, "extracted_*");
                foreach (var dir in extractDirs)
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (dirInfo.CreationTime < DateTime.Now.AddDays(-1))
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

                foreach (var file in Directory.GetFiles(latestBackup))
                {
                    var fileName = Path.GetFileName(file);
                    var targetFile = Path.Combine(appDirectory, fileName);
                    File.Copy(file, targetFile, true);
                }

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