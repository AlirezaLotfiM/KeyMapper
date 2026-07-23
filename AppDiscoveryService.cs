using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KeyMapper
{
    public class InstalledAppInfo
    {
        public string Name { get; set; } = string.Empty;
        public string InstallLocation { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
    }

    public class AppDiscoveryService
    {
        private static readonly Lazy<AppDiscoveryService> _instance = new(() => new AppDiscoveryService());
        public static AppDiscoveryService Instance => _instance.Value;

        private readonly List<InstalledAppInfo> _cachedApps = new();
        private DateTime _lastScanTime = DateTime.MinValue;

        public List<InstalledAppInfo> GetInstalledApps(bool forceRefresh = false)
        {
            if (!forceRefresh && _cachedApps.Count > 0 && (DateTime.Now - _lastScanTime).TotalMinutes < 30)
            {
                return _cachedApps;
            }

            _cachedApps.Clear();

            string[] registryKeys = new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
            };

            // Scan HKEY_LOCAL_MACHINE & HKEY_CURRENT_USER
            ScanRegistryHive(Registry.LocalMachine, registryKeys);
            ScanRegistryHive(Registry.CurrentUser, registryKeys);
            ScanAppPaths(Registry.LocalMachine);
            ScanAppPaths(Registry.CurrentUser);

            _lastScanTime = DateTime.Now;
            return _cachedApps;
        }

        private void ScanAppPaths(RegistryKey rootKey)
        {
            using RegistryKey? pathsKey = rootKey.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
            if (pathsKey == null) return;

            foreach (string subKeyName in pathsKey.GetSubKeyNames())
            {
                using RegistryKey? appKey = pathsKey.OpenSubKey(subKeyName);
                string executable = appKey?.GetValue(null) as string ?? string.Empty;
                executable = executable.Trim().Trim('"');
                if (!File.Exists(executable)) continue;

                string name = Path.GetFileNameWithoutExtension(subKeyName);
                if (_cachedApps.Any(app =>
                    string.Equals(app.ExecutablePath, executable, StringComparison.OrdinalIgnoreCase)))
                    continue;

                _cachedApps.Add(new InstalledAppInfo
                {
                    Name = name,
                    InstallLocation = Path.GetDirectoryName(executable) ?? string.Empty,
                    ExecutablePath = executable
                });
            }
        }

        private void ScanRegistryHive(RegistryKey rootKey, string[] keysToScan)
        {
            foreach (string subKeyPath in keysToScan)
            {
                using RegistryKey? key = rootKey.OpenSubKey(subKeyPath);
                if (key == null) continue;

                foreach (string subKeyName in key.GetSubKeyNames())
                {
                    using RegistryKey? appKey = key.OpenSubKey(subKeyName);
                    if (appKey == null) continue;

                    string? displayName = appKey.GetValue("DisplayName") as string;
                    string? installLocation = appKey.GetValue("InstallLocation") as string;
                    string? displayIcon = appKey.GetValue("DisplayIcon") as string;

                    if (string.IsNullOrWhiteSpace(displayName)) continue;

                    string exePath = ResolveExecutablePath(installLocation, displayIcon);

                    if (!_cachedApps.Any(a => string.Equals(a.Name, displayName, StringComparison.OrdinalIgnoreCase)))
                    {
                        _cachedApps.Add(new InstalledAppInfo
                        {
                            Name = displayName,
                            InstallLocation = installLocation ?? string.Empty,
                            ExecutablePath = exePath
                        });
                    }
                }
            }
        }

        private string ResolveExecutablePath(string? installLocation, string? displayIcon)
        {
            if (!string.IsNullOrWhiteSpace(displayIcon))
            {
                string iconPath = displayIcon.Split(',')[0].Trim('"');
                if (iconPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && File.Exists(iconPath))
                {
                    return iconPath;
                }
            }

            if (!string.IsNullOrWhiteSpace(installLocation) && Directory.Exists(installLocation))
            {
                var exeFiles = Directory.GetFiles(installLocation, "*.exe", SearchOption.TopDirectoryOnly);
                if (exeFiles.Length > 0)
                {
                    return exeFiles[0];
                }
            }

            return string.Empty;
        }

        public bool LaunchApplication(string appName, out string statusMessage)
        {
            InstalledAppInfo? match = FindApplication(appName);

            if (match != null && !string.IsNullOrEmpty(match.ExecutablePath) && File.Exists(match.ExecutablePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = match.ExecutablePath,
                        UseShellExecute = true
                    });
                    statusMessage = $"Launched '{match.Name}'.";
                    return true;
                }
                catch (Exception ex)
                {
                    statusMessage = $"Failed to launch {match.Name}: {ex.Message}";
                    return false;
                }
            }

            statusMessage = $"Application '{appName}' not found in Windows Registry.";
            return false;
        }

        public InstalledAppInfo? FindApplication(string appName, bool forceRefresh = false)
        {
            string query = NormalizeName(appName);
            if (query.Length == 0) return null;

            return GetInstalledApps(forceRefresh)
                .Where(app => !string.IsNullOrWhiteSpace(app.ExecutablePath) &&
                              File.Exists(app.ExecutablePath))
                .OrderBy(app => Math.Abs(NormalizeName(app.Name).Length - query.Length))
                .FirstOrDefault(app =>
                {
                    string candidate = NormalizeName(app.Name);
                    return candidate.Equals(query, StringComparison.OrdinalIgnoreCase) ||
                           candidate.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                           query.Contains(candidate, StringComparison.OrdinalIgnoreCase);
                });
        }

        private static string NormalizeName(string value) =>
            Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", string.Empty);
    }
}
