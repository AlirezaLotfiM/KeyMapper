using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace KeyMapper
{
    public class SteamGameInfo
    {
        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string InstallDir { get; set; } = string.Empty;
    }

    public class SteamAutomationService
    {
        private static readonly Lazy<SteamAutomationService> _instance = new(() => new SteamAutomationService());
        public static SteamAutomationService Instance => _instance.Value;

        public List<SteamGameInfo> GetInstalledSteamGames()
        {
            var games = new List<SteamGameInfo>();
            string steamPath = GetSteamInstallPath();

            if (string.IsNullOrEmpty(steamPath) || !Directory.Exists(steamPath))
                return games;

            List<string> libraryPaths = GetSteamLibraryFolders(steamPath);

            foreach (string libPath in libraryPaths)
            {
                string steamAppsDir = Path.Combine(libPath, "steamapps");
                if (!Directory.Exists(steamAppsDir)) continue;

                var manifestFiles = Directory.GetFiles(steamAppsDir, "appmanifest_*.acf");
                foreach (string manifestFile in manifestFiles)
                {
                    var gameInfo = ParseManifest(manifestFile);
                    if (gameInfo != null && !string.IsNullOrEmpty(gameInfo.AppId))
                    {
                        games.Add(gameInfo);
                    }
                }
            }

            return games;
        }

        public bool LaunchGame(string gameQuery, out string statusMessage)
        {
            SteamGameInfo? match = FindInstalledGame(gameQuery);

            if (match != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = $"steam://run/{match.AppId}",
                        UseShellExecute = true
                    });
                    statusMessage = $"Launching '{match.Name}' via Steam...";
                    return true;
                }
                catch (Exception ex)
                {
                    statusMessage = $"Failed to launch Steam game: {ex.Message}";
                    return false;
                }
            }

            statusMessage = $"'{gameQuery}' is not installed in any local Steam library.";
            return false;
        }

        public SteamGameInfo? FindInstalledGame(string gameQuery)
        {
            string normalizedQuery = NormalizeName(gameQuery);
            if (normalizedQuery.Length == 0) return null;
            return GetInstalledSteamGames()
                .OrderBy(game => Math.Abs(game.Name.Length - gameQuery.Length))
                .FirstOrDefault(game =>
                {
                    string normalizedName = NormalizeName(game.Name);
                    return normalizedName.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
                           normalizedQuery.Contains(normalizedName, StringComparison.OrdinalIgnoreCase);
                });
        }

        public bool IsSteamInstalled() =>
            File.Exists(GetSteamExecutablePath());

        public string GetSteamExecutablePath()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string registryExecutable = key?.GetValue("SteamExe") as string ?? string.Empty;
            registryExecutable = registryExecutable.Replace('/', '\\').Trim('"');
            if (File.Exists(registryExecutable)) return registryExecutable;

            string installPath = GetSteamInstallPath();
            string executable = Path.Combine(installPath, "steam.exe");
            return File.Exists(executable) ? executable : string.Empty;
        }

        public bool LaunchSteamClient(out string statusMessage)
        {
            string executable = GetSteamExecutablePath();
            if (executable.Length == 0)
            {
                statusMessage = "Steam is not installed on this computer.";
                return false;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    UseShellExecute = true
                });
                statusMessage = "Steam is opening.";
                return true;
            }
            catch (Exception ex)
            {
                statusMessage = $"Steam could not be opened: {ex.Message}";
                return false;
            }
        }

        public string GetSteamInstallPath()
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
            string registryPath = key?.GetValue("SteamPath") as string ?? string.Empty;
            registryPath = registryPath.Replace('/', '\\');
            if (Directory.Exists(registryPath)) return registryPath;

            string[] commonPaths =
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    "Steam"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Steam")
            ];
            return commonPaths.FirstOrDefault(Directory.Exists) ?? string.Empty;
        }

        private static string NormalizeName(string value) =>
            Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{Nd}]+", string.Empty);

        private List<string> GetSteamLibraryFolders(string steamPath)
        {
            var folders = new List<string> { steamPath };
            string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");

            if (File.Exists(vdfPath))
            {
                string text = File.ReadAllText(vdfPath);
                var matches = Regex.Matches(text, @"""path""\s+""([^""]+)""");
                foreach (Match match in matches)
                {
                    string path = match.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(path) && !folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                    {
                        folders.Add(path);
                    }
                }
            }

            return folders;
        }

        private SteamGameInfo? ParseManifest(string manifestPath)
        {
            try
            {
                string content = File.ReadAllText(manifestPath);
                string appId = Regex.Match(content, @"""appid""\s+""(\d+)""").Groups[1].Value;
                string name = Regex.Match(content, @"""name""\s+""([^""]+)""").Groups[1].Value;
                string installDir = Regex.Match(content, @"""installdir""\s+""([^""]+)""").Groups[1].Value;

                if (!string.IsNullOrEmpty(appId) && !string.IsNullOrEmpty(name))
                {
                    return new SteamGameInfo
                    {
                        AppId = appId,
                        Name = name,
                        InstallDir = installDir
                    };
                }
            }
            catch { }

            return null;
        }
    }
}
