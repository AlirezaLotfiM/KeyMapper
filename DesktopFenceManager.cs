using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;

namespace KeyMapper
{
    public sealed class DesktopFenceManager
    {
        private static readonly Lazy<DesktopFenceManager> LazyInstance = new(() => new DesktopFenceManager());
        public static DesktopFenceManager Instance => LazyInstance.Value;

        private readonly string _appDataDir;
        private readonly string _fencesFilePath;
        private readonly Dictionary<string, DesktopFenceWindow> _openWindows = new();
        private DesktopFencesContainer _container = new();

        public bool AreFencesVisible => _container.AreFencesVisible;
        public IReadOnlyList<DesktopFenceConfig> Fences => _container.Fences.AsReadOnly();

        public event Action? OnFencesUpdated;

        private DesktopFenceManager()
        {
            _appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyMapper");
            Directory.CreateDirectory(_appDataDir);
            _fencesFilePath = Path.Combine(_appDataDir, "desktop_fences.json");
        }

        private static void EnsureOnDispatcher(Action action)
        {
            if (Application.Current != null)
            {
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(action);
                }
                else
                {
                    action();
                }
            }
        }

        public void Initialize()
        {
            EnsureOnDispatcher(() =>
            {
                LoadFences();

                if (_container.Fences.Count == 0)
                {
                    CreateDefaultFences();
                }

                if (_container.AreFencesVisible)
                {
                    ShowAllFencesInternal();
                }
            });
        }

        private void CreateDefaultFences()
        {
            var fence1 = new DesktopFenceConfig
            {
                Title = "⚡ Quick Launch",
                Type = FenceType.CustomShortcuts,
                Left = 80,
                Top = 120,
                Width = 290,
                Height = 320,
                ColorTheme = "Lavender",
                FenceOpacity = 0.85,
                Items = new List<DesktopFenceItem>
                {
                    new DesktopFenceItem { Title = "Notepad", TargetPath = "notepad.exe" },
                    new DesktopFenceItem { Title = "Calculator", TargetPath = "calc.exe" },
                    new DesktopFenceItem { Title = "Command Prompt", TargetPath = "cmd.exe" }
                }
            };

            string userDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            var fence2 = new DesktopFenceConfig
            {
                Title = "📁 Downloads Portal",
                Type = Directory.Exists(userDownloads) ? FenceType.FolderPortal : FenceType.CustomShortcuts,
                FolderPortalPath = userDownloads,
                Left = 400,
                Top = 120,
                Width = 320,
                Height = 360,
                ColorTheme = "Pastel Pink",
                FenceOpacity = 0.85
            };

            _container.Fences.Add(fence1);
            _container.Fences.Add(fence2);
            SaveFences();
        }

        public void LoadFences()
        {
            if (File.Exists(_fencesFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_fencesFilePath);
                    var loaded = JsonSerializer.Deserialize<DesktopFencesContainer>(json);
                    if (loaded != null)
                    {
                        _container = loaded;
                    }
                }
                catch { }
            }
        }

        public void SaveFences()
        {
            try
            {
                EnsureOnDispatcher(() =>
                {
                    foreach (var kvp in _openWindows)
                    {
                        var win = kvp.Value;
                        var config = _container.Fences.FirstOrDefault(f => f.Id == kvp.Key);
                        if (config != null && win.IsLoaded)
                        {
                            config.Left = win.Left;
                            config.Top = win.Top;
                            config.Width = win.Width;
                            config.Height = win.Height;
                            config.IsCollapsed = win.IsCollapsed;
                        }
                    }
                });

                string json = JsonSerializer.Serialize(_container, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_fencesFilePath, json);
            }
            catch { }

            EnsureOnDispatcher(() => OnFencesUpdated?.Invoke());
        }

        public void ShowAllFences()
        {
            EnsureOnDispatcher(() =>
            {
                ShowAllFencesInternal();
            });
        }

        private void ShowAllFencesInternal()
        {
            _container.AreFencesVisible = true;
            foreach (var config in _container.Fences)
            {
                ShowFenceWindowInternal(config);
            }
            SaveFences();
        }

        public void HideAllFences()
        {
            EnsureOnDispatcher(() =>
            {
                _container.AreFencesVisible = false;
                foreach (var win in _openWindows.Values.ToList())
                {
                    win.Close();
                }
                _openWindows.Clear();
                SaveFences();
            });
        }

        public void ToggleFencesVisibility()
        {
            EnsureOnDispatcher(() =>
            {
                if (_container.AreFencesVisible)
                {
                    HideAllFences();
                }
                else
                {
                    ShowAllFencesInternal();
                }
            });
        }

        public DesktopFenceConfig CreateNewFence(string title = "New Fence", FenceType type = FenceType.CustomShortcuts, string folderPath = "")
        {
            DesktopFenceConfig config = null!;
            EnsureOnDispatcher(() =>
            {
                config = new DesktopFenceConfig
                {
                    Title = string.IsNullOrWhiteSpace(title) ? "New Fence" : title,
                    Type = type,
                    FolderPortalPath = folderPath,
                    Left = 150 + (_container.Fences.Count * 30),
                    Top = 150 + (_container.Fences.Count * 30),
                    Width = 300,
                    Height = 340,
                    ColorTheme = GetRandomTheme()
                };

                _container.Fences.Add(config);
                _container.AreFencesVisible = true;
                SaveFences();
                ShowFenceWindowInternal(config);
            });

            return config;
        }

        public void RemoveFence(string fenceId)
        {
            EnsureOnDispatcher(() =>
            {
                if (_openWindows.TryGetValue(fenceId, out var win))
                {
                    _openWindows.Remove(fenceId);
                    win.Close();
                }

                _container.Fences.RemoveAll(f => f.Id == fenceId);
                SaveFences();
            });
        }

        public void RegisterWindowClosed(string fenceId)
        {
            _openWindows.Remove(fenceId);
        }

        private void ShowFenceWindowInternal(DesktopFenceConfig config)
        {
            if (_openWindows.TryGetValue(config.Id, out var existingWindow))
            {
                existingWindow.Show();
                existingWindow.WindowState = WindowState.Normal;
                existingWindow.Activate();
                return;
            }

            var window = new DesktopFenceWindow(config);
            _openWindows[config.Id] = window;
            window.Show();
            window.Activate();
        }

        private static string GetRandomTheme()
        {
            string[] themes = { "Lavender", "Sky Blue", "Pastel Pink", "Soft Mint", "Warm Yellow", "Dark Carbon" };
            return themes[Random.Shared.Next(themes.Length)];
        }
    }
}
