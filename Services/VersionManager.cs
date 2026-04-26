using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
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
        // Use AppData for updates - this is always writable
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WorkPulse");

        private static readonly string UpdateFolder = Path.Combine(AppDataFolder, "updates");
        private static readonly string BackupFolder = Path.Combine(AppDataFolder, "backup");
        private static readonly string VersionFilePath = Path.Combine(AppDataFolder, "version.json");

        // Get the actual application directory (where the EXE is running from)
        private static string GetApplicationDirectory()
        {
            return Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        }

        private readonly HttpClient _httpClient;

        private const string GITHUB_OWNER = "Karthick256";
        private const string GITHUB_REPO = "WORKPULSE-DESKTOP";
        private const string GITHUB_API_URL = $"https://api.github.com/repos/{GITHUB_OWNER}/{GITHUB_REPO}/releases/latest";

        public VersionManager()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "WorkPulse-AutoUpdater");
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            // Create writable directories in AppData
            if (!Directory.Exists(UpdateFolder))
                Directory.CreateDirectory(UpdateFolder);
            if (!Directory.Exists(BackupFolder))
                Directory.CreateDirectory(BackupFolder);

            Debug.WriteLine($"App Directory: {GetApplicationDirectory()}");
            Debug.WriteLine($"Update Folder: {UpdateFolder}");
            Debug.WriteLine($"Can Write to App Dir: {CanWriteToDirectory(GetApplicationDirectory())}");
        }

        private bool CanWriteToDirectory(string directory)
        {
            try
            {
                string testFile = Path.Combine(directory, "test_write.tmp");
                File.WriteAllText(testFile, "test");
                File.Delete(testFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public Version GetCurrentVersion()
        {
            // First try to read from AppData version file
            if (File.Exists(VersionFilePath))
            {
                try
                {
                    var json = File.ReadAllText(VersionFilePath);
                    var versionInfo = JsonSerializer.Deserialize<VersionInfo>(json);
                    if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.Version))
                    {
                        Debug.WriteLine($"Version from AppData: {versionInfo.Version}");
                        return Version.Parse(versionInfo.Version);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error reading version file: {ex.Message}");
                }
            }

            // Fall back to assembly version
            var assembly = Assembly.GetExecutingAssembly();
            var fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            var version = new Version(fileVersionInfo.FileVersion ?? "1.0.0");
            Debug.WriteLine($"Version from Assembly: {version}");

            // Save to AppData for future reference
            SaveVersionInfo(version.ToString());
            return version;
        }

        public async Task<UpdateInfo> CheckForUpdatesAsync()
        {
            try
            {
                var currentVersion = GetCurrentVersion();
                Debug.WriteLine($"Current version: {currentVersion}");

                // Add a user agent and accept header for GitHub API
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "WorkPulse-AutoUpdater/1.0");
                _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

                var response = await _httpClient.GetAsync(GITHUB_API_URL);

                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"GitHub API error: {response.StatusCode}");
                    return new UpdateInfo { HasUpdate = false };
                }

                var json = await response.Content.ReadAsStringAsync();
                Debug.WriteLine($"GitHub API response received");

                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var latestTag = root.GetProperty("tag_name").GetString();
                Debug.WriteLine($"Latest tag: {latestTag}");

                if (string.IsNullOrEmpty(latestTag))
                {
                    return new UpdateInfo { HasUpdate = false };
                }

                var latestVersion = ParseVersionFromTag(latestTag);

                // Get the download URL for the zip file
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

                // Get release notes
                string releaseNotes = null;
                if (root.TryGetProperty("body", out var bodyElement))
                {
                    releaseNotes = bodyElement.GetString();
                }

                var hasUpdate = latestVersion > currentVersion;
                Debug.WriteLine($"Has update: {hasUpdate}");

                return new UpdateInfo
                {
                    HasUpdate = hasUpdate,
                    CurrentVersion = new VersionInfo
                    {
                        Version = currentVersion.ToString(),
                        ReleaseDate = DateTime.Now,
                        IsMandatory = false
                    },
                    LatestVersion = hasUpdate ? new VersionInfo
                    {
                        Version = latestVersion.ToString(),
                        ReleaseUrl = downloadUrl,
                        ReleaseNotes = releaseNotes ?? "No release notes available.",
                        ReleaseDate = DateTime.Now,
                        IsMandatory = IsMajorUpdate(currentVersion, latestVersion)
                    } : null
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking for updates: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
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

                // Create update script
                var scriptPath = CreateUpdateScript(extractPath, updateInfo.Version);

                progress?.Report(95);

                // Launch the update script as administrator if needed
                var startInfo = new ProcessStartInfo
                {
                    FileName = scriptPath,
                    UseShellExecute = true,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal,
                    Verb = "runas" // Request admin rights for the updater
                };

                Process.Start(startInfo);

                progress?.Report(100);

                // Save the new version info
                SaveVersionInfo(updateInfo.Version);

                // Shutdown the current app
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.Shutdown();
                });

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
            var currentDirectory = GetApplicationDirectory();
            var appDataFolder = AppDataFolder;

            var scriptPath = Path.Combine(Path.GetTempPath(), $"workpulse_update_{DateTime.Now:yyyyMMddHHmmss}.bat");

            var scriptContent = $@"
@echo off
title WorkPulse Updater
echo ========================================
echo WorkPulse Updater
echo ========================================
echo.
echo Updating WorkPulse...
echo.

:: Wait for the main app to close
timeout /t 3 /nobreak > nul

:: Kill any remaining WorkPulse processes
taskkill /f /im {currentExeName} > nul 2>&1

echo Copying new files...
:: Copy files from the extracted update to the application directory
xcopy ""{extractPath}\*"" ""{currentDirectory}\"" /E /Y /I /Q /H /R

echo.
echo Cleaning up...
:: Clean up update files
rmdir /s /q ""{extractPath}"" 2>nul
del ""{extractPath.Replace("extracted", "update")}.zip"" 2>nul

echo.
echo Update complete! Starting WorkPulse...
timeout /t 2 /nobreak > nul

:: Start the updated application
start """" ""{currentExePath}""

:: Delete the updater script
timeout /t 2 /nobreak > nul
del ""%~f0""
";

            File.WriteAllText(scriptPath, scriptContent, System.Text.Encoding.UTF8);
            Debug.WriteLine($"Created update script at: {scriptPath}");
            return scriptPath;
        }

        private void SaveVersionInfo(string version)
        {
            try
            {
                var versionInfo = new VersionInfo
                {
                    Version = version,
                    ReleaseDate = DateTime.Now
                };

                var json = JsonSerializer.Serialize(versionInfo);
                File.WriteAllText(VersionFilePath, json);
                Debug.WriteLine($"Saved version info: {version} to {VersionFilePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save version info: {ex.Message}");
            }
        }

        public void CleanupOldUpdates()
        {
            try
            {
                if (Directory.Exists(UpdateFolder))
                {
                    var updateFiles = Directory.GetFiles(UpdateFolder, "update_*.zip");
                    foreach (var file in updateFiles)
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < DateTime.Now.AddDays(-7))
                        {
                            File.Delete(file);
                            Debug.WriteLine($"Deleted old update file: {file}");
                        }
                    }

                    var extractDirs = Directory.GetDirectories(UpdateFolder, "extracted_*");
                    foreach (var dir in extractDirs)
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        if (dirInfo.CreationTime < DateTime.Now.AddDays(-1))
                        {
                            Directory.Delete(dir, true);
                            Debug.WriteLine($"Deleted old extracted folder: {dir}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup error: {ex.Message}");
            }
        }
    }
}