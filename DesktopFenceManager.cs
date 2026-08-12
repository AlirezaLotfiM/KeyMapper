using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;

namespace KeyMapper
{
    public class DesktopFenceManager
    {
        private static readonly Lazy<DesktopFenceManager> _instance = new Lazy<DesktopFenceManager>(() => new DesktopFenceManager());
        public static DesktopFenceManager Instance => _instance.Value;

        private readonly string _configFilePath;
        private DesktopFencesContainer _container = new DesktopFencesContainer();
        private readonly Dictionary<string, DesktopFenceWindow> _activeWindows = new Dictionary<string, DesktopFenceWindow>();

        public bool AreFencesVisible => _container.AreFencesVisible;
        public IReadOnlyList<DesktopFenceConfig> Fences => _container.Fences.AsReadOnly();

        private DesktopFenceManager()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dir = Path.Combine(appData, "KeyMapper");
            Directory.CreateDirectory(dir);
            _configFilePath = Path.Combine(dir, "desktop_fences.json");
        }

        public void Initialize()
        {
            EnsureOnDispatcher(() =>
            {
                LoadFences();

                if (_container.AreFencesVisible && _container.Fences.Count > 0)
                {
                    ShowAllFencesInternal();
                }
            });
        }

        private void LoadFences()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    string json = File.ReadAllText(_configFilePath);
                    var loaded = JsonSerializer.Deserialize<DesktopFencesContainer>(json);
                    if (loaded != null)
                    {
                        _container = loaded;
                    }
                }
            }
            catch
            {
                _container = new DesktopFencesContainer();
            }
        }

        public void SaveFences()
        {
            try
            {
                string json = JsonSerializer.Serialize(_container, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }

        public void CreateFence(string title = "New Fence", FenceType type = FenceType.CustomShortcuts)
        {
            EnsureOnDispatcher(() =>
            {
                var config = new DesktopFenceConfig
                {
                    Title = title,
                    Type = type,
                    Left = 200 + (_container.Fences.Count * 20),
                    Top = 180 + (_container.Fences.Count * 20),
                    Width = 310,
                    Height = 340,
                    ColorTheme = "Warm Yellow"
                };

                _container.Fences.Add(config);
                SaveFences();

                if (_container.AreFencesVisible)
                {
                    var win = new DesktopFenceWindow(config);
                    _activeWindows[config.Id] = win;
                    win.Show();
                }
            });
        }

        public void CreateNewFence() => CreateFence();

        public void CreateFolderPortalFence(string title, string folderPath)
        {
            EnsureOnDispatcher(() =>
            {
                var config = new DesktopFenceConfig
                {
                    Title = string.IsNullOrWhiteSpace(title) ? "Folder Portal" : title,
                    Type = FenceType.FolderPortal,
                    FolderPortalPath = folderPath,
                    Left = 200 + (_container.Fences.Count * 20),
                    Top = 180 + (_container.Fences.Count * 20),
                    Width = 310,
                    Height = 340,
                    ColorTheme = "Warm Yellow"
                };

                _container.Fences.Add(config);
                SaveFences();

                if (_container.AreFencesVisible)
                {
                    var win = new DesktopFenceWindow(config);
                    _activeWindows[config.Id] = win;
                    win.Show();
                }
            });
        }

        public void RemoveFence(string id)
        {
            EnsureOnDispatcher(() =>
            {
                if (_activeWindows.TryGetValue(id, out var win))
                {
                    _activeWindows.Remove(id);
                    win.Close();
                }

                _container.Fences.RemoveAll(x => x.Id == id);
                SaveFences();
            });
        }

        public void ToggleVisibility()
        {
            EnsureOnDispatcher(() =>
            {
                _container.AreFencesVisible = !_container.AreFencesVisible;
                SaveFences();

                if (_container.AreFencesVisible)
                {
                    ShowAllFencesInternal();
                }
                else
                {
                    HideAllFencesInternal();
                }
            });
        }

        public void ToggleFencesVisibility() => ToggleVisibility();

        private void ShowAllFencesInternal()
        {
            foreach (var config in _container.Fences)
            {
                if (!_activeWindows.ContainsKey(config.Id))
                {
                    var win = new DesktopFenceWindow(config);
                    _activeWindows[config.Id] = win;
                    win.Show();
                }
                else
                {
                    _activeWindows[config.Id].Show();
                }
            }
        }

        private void HideAllFencesInternal()
        {
            foreach (var kvp in _activeWindows.ToList())
            {
                kvp.Value.Close();
            }
            _activeWindows.Clear();
        }

        public void RegisterWindowClosed(string id)
        {
            _activeWindows.Remove(id);
        }

        private void EnsureOnDispatcher(Action action)
        {
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    action();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(action);
                }
            }
            else
            {
                action();
            }
        }
    }
}
