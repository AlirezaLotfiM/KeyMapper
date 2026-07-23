using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace KeyMapper
{
    public class ShortcutConfig
    {
        public string Shortcut { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public bool IsAutoExpand { get; set; }
        public string AllowedProcess { get; set; } = string.Empty;
        public string ExcludedProcess { get; set; } = string.Empty;
        public int UsageCount { get; set; }
    }

    public class AppSettings
    {
        public List<ShortcutConfig> Replacements { get; set; } = new List<ShortcutConfig>();
        public List<ShortcutConfig> Actions { get; set; } = new List<ShortcutConfig>();
        public List<string> ExcludedProcesses { get; set; } = new List<string>();
        public List<string> AutoExpandShortcuts { get; set; } = new List<string>();
        public bool SuppressKeysDuringRecording { get; set; } = true;
        public bool ShowOverlay { get; set; } = true;
        public bool RunAtStartup { get; set; } = false;
        public bool PlaySounds { get; set; } = true;
        public bool ShowPetOverlay { get; set; } = true;
        public string ThemeName { get; set; } = "Warm Cream";
        public double PetWalkingSpeed { get; set; } = 92;
        public int PetIdleAnimationIntervalMs { get; set; } = 430;
        public bool PetCommentsEnabled { get; set; } = true;
        public string PetCommentFrequency { get; set; } = "Normal";
        public bool AiAmbientCommentsEnabled { get; set; } = true;
        public bool LocalAiEnabled { get; set; } = true;
        public string LocalAiModelId { get; set; } = string.Empty;
        public string AiApiKey { get; set; } = string.Empty;
        public string AiApiEndpoint { get; set; } = string.Empty;
        public string AiModel { get; set; } = "gpt-4o-mini";
        public string LibreTranslateEndpoint { get; set; } = "http://localhost:5000";
        public string LibreTranslateApiKey { get; set; } = string.Empty;
        public int LibreTranslateLiveDelayMs { get; set; } = 650;
        public bool LibreTranslateAutoCopy { get; set; } = false;
        public string LayoutFixHotkey { get; set; } = "Ctrl+Alt+K";
        public string CurrentCharacter { get; set; } = "Pink Monster";
    }

    public class OldAppSettings
    {
        public Dictionary<string, string> Replacements { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> Actions { get; set; } = new Dictionary<string, string>();
        public List<string> ExcludedProcesses { get; set; } = new List<string>();
        public List<string> AutoExpandShortcuts { get; set; } = new List<string>();
        public bool SuppressKeysDuringRecording { get; set; } = true;
        public bool ShowOverlay { get; set; } = true;
        public bool RunAtStartup { get; set; } = false;
        public bool PlaySounds { get; set; } = true;
    }

    public static class ConfigManager
    {
        private static readonly string FolderPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyMapper"
        );
        private static readonly string FilePath = Path.Combine(FolderPath, "config.json");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);

                    // Perform in-memory migration if old schema is detected
                    bool isOldFormat = false;
                    using (JsonDocument doc = JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        if (root.TryGetProperty("Replacements", out JsonElement repElement))
                        {
                            if (repElement.ValueKind == JsonValueKind.Object)
                            {
                                isOldFormat = true;
                            }
                        }
                    }

                    if (isOldFormat)
                    {
                        var oldSettings = JsonSerializer.Deserialize<OldAppSettings>(json);
                        if (oldSettings != null)
                        {
                            var newSettings = new AppSettings
                            {
                                SuppressKeysDuringRecording = oldSettings.SuppressKeysDuringRecording,
                                ShowOverlay = oldSettings.ShowOverlay,
                                RunAtStartup = oldSettings.RunAtStartup,
                                PlaySounds = oldSettings.PlaySounds,
                                ExcludedProcesses = oldSettings.ExcludedProcesses ?? new List<string>()
                            };

                            if (oldSettings.Replacements != null)
                            {
                                foreach (var kvp in oldSettings.Replacements)
                                {
                                    bool isAuto = oldSettings.AutoExpandShortcuts?.Contains(kvp.Key) ?? false;
                                    newSettings.Replacements.Add(new ShortcutConfig
                                    {
                                        Shortcut = kvp.Key,
                                        Target = kvp.Value,
                                        IsAutoExpand = isAuto
                                    });
                                }
                            }

                            if (oldSettings.Actions != null)
                            {
                                foreach (var kvp in oldSettings.Actions)
                                {
                                    newSettings.Actions.Add(new ShortcutConfig
                                    {
                                        Shortcut = kvp.Key,
                                        Target = kvp.Value
                                    });
                                }
                            }

                            // Sync list
                            var autoList = new List<string>();
                            foreach (var r in newSettings.Replacements)
                            {
                                if (r.IsAutoExpand) autoList.Add(r.Shortcut);
                            }
                            newSettings.AutoExpandShortcuts = autoList;

                            Save(newSettings); // Convert on disk!
                            return newSettings;
                        }
                    }

                    var settings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (settings != null)
                    {
                        settings.Replacements ??= new List<ShortcutConfig>();
                        settings.Actions ??= new List<ShortcutConfig>();
                        settings.ExcludedProcesses ??= new List<string>();
                        settings.AutoExpandShortcuts ??= new List<string>();
                        return settings;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error loading config: {ex.Message}");
            }

            // Return default settings if loading fails or file does not exist
            var defaults = GetDefaultSettings();
            Save(defaults);
            return defaults;
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                if (!Directory.Exists(FolderPath))
                {
                    Directory.CreateDirectory(FolderPath);
                }

                string json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(FilePath, json);

                // Update registry for startup
                SetStartupRegistry(settings.RunAtStartup);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving config: {ex.Message}");
            }
        }

        private static AppSettings GetDefaultSettings()
        {
            return new AppSettings
            {
                Replacements = new List<ShortcutConfig>
                {
                    new ShortcutConfig { Shortcut = "mail", Target = "mymail@mail.ir" },
                    new ShortcutConfig { Shortcut = "shg", Target = "shrug (¯\\_(ツ)_/¯)" },
                    new ShortcutConfig { Shortcut = "tag", Target = "<a>{cursor}</a>" },
                    new ShortcutConfig { Shortcut = "dt", Target = "Today is {date} at {time}" },
                    new ShortcutConfig { Shortcut = "paste", Target = "Clipboard content: \"{clip}\"" },
                    new ShortcutConfig { Shortcut = "adr;", Target = "123 Main Street, New York", IsAutoExpand = true }
                },
                Actions = new List<ShortcutConfig>
                {
                    new ShortcutConfig { Shortcut = "tlg", Target = "telegram.exe" },
                    new ShortcutConfig { Shortcut = "calc", Target = "calc.exe" }
                },
                ExcludedProcesses = new List<string>
                {
                    "gta5.exe",
                    "valorant.exe"
                },
                AutoExpandShortcuts = new List<string>
                {
                    "adr;"
                },
                SuppressKeysDuringRecording = true,
                ShowOverlay = true,
                RunAtStartup = false,
                PlaySounds = true
            };
        }

        private static void SetStartupRegistry(bool runAtStartup)
        {
            try
            {
                string path = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
                using (RegistryKey? key = Registry.CurrentUser.OpenSubKey(path, true))
                {
                    if (key != null)
                    {
                        var mainModule = Process.GetCurrentProcess().MainModule;
                        if (mainModule != null && !string.IsNullOrEmpty(mainModule.FileName))
                        {
                            string appPath = mainModule.FileName;
                            if (runAtStartup)
                            {
                                key.SetValue("KeyMapper", $"\"{appPath}\" --minimized");
                            }
                            else
                            {
                                key.DeleteValue("KeyMapper", false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set startup registry key: {ex.Message}");
            }
        }
    }
}
