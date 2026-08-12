using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KeyMapper
{
    public partial class VpnService : ObservableObject
    {
        private static readonly Lazy<VpnService> _instance = new Lazy<VpnService>(() => new VpnService());
        public static VpnService Instance => _instance.Value;

        public CoreManagerService CoreManager { get; } = new CoreManagerService();
        public VpnSettings Settings { get; private set; }

        public ObservableCollection<VpnServerProfile> Servers { get; } = new ObservableCollection<VpnServerProfile>();
        public ObservableCollection<VpnSubscription> Subscriptions { get; } = new ObservableCollection<VpnSubscription>();
        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        [ObservableProperty]
        private bool _isConnected = false;

        [ObservableProperty]
        private bool _isConnecting = false;

        [ObservableProperty]
        private string _statusText = "Disconnected";

        [ObservableProperty]
        private VpnServerProfile? _activeServer;

        [ObservableProperty]
        private TrafficStats _traffic = new TrafficStats();

        public event Action? StateChanged;

        private VpnService()
        {
            Settings = VpnStorageService.LoadSettings();

            var loadedServers = VpnStorageService.LoadServers();
            if (loadedServers != null)
            {
                foreach (var s in loadedServers) Servers.Add(s);
            }

            var loadedSubs = VpnStorageService.LoadSubscriptions();
            if (loadedSubs != null)
            {
                foreach (var sub in loadedSubs) Subscriptions.Add(sub);
            }

            if (!string.IsNullOrEmpty(Settings.ActiveServerId))
            {
                ActiveServer = Servers.FirstOrDefault(s => s.Id == Settings.ActiveServerId);
            }
            if (ActiveServer == null && Servers.Count > 0)
            {
                ActiveServer = Servers[0];
            }

            CoreManager.LogReceived += msg =>
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}");
                        if (Logs.Count > 1000) Logs.RemoveAt(0);
                    });
                }
            };

            CoreManager.TrafficUpdated += stats =>
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        Traffic = stats;
                    });
                }
            };

            CoreManager.UnexpectedExit += () =>
            {
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        IsConnected = false;
                        IsConnecting = false;
                        StatusText = "Disconnected (Process Exited)";
                        StateChanged?.Invoke();
                    });
                }
            };
        }

        public void SaveSettings()
        {
            VpnStorageService.SaveSettings(Settings);
        }

        public void SaveServers()
        {
            VpnStorageService.SaveServers(Servers);
        }

        public void SaveSubscriptions()
        {
            VpnStorageService.SaveSubscriptions(Subscriptions);
        }

        public async Task<bool> ToggleConnectionAsync()
        {
            if (IsConnecting) return false;

            if (IsConnected)
            {
                return await DisconnectAsync();
            }
            else
            {
                if (ActiveServer == null)
                {
                    StatusText = "No Server Selected";
                    StateChanged?.Invoke();
                    return false;
                }
                return await ConnectAsync(ActiveServer);
            }
        }

        public async Task<bool> ConnectAsync(VpnServerProfile server)
        {
            if (IsConnecting) return false;

            try
            {
                ActiveServer = server;
                Settings.ActiveServerId = server.Id;
                SaveSettings();

                IsConnecting = true;
                StatusText = $"Connecting to {server.Name}...";
                StateChanged?.Invoke();

                var success = await CoreManager.StartAsync(server, Settings);
                IsConnecting = false;

                if (success)
                {
                    IsConnected = true;
                    StatusText = $"Connected • {server.Name}";
                }
                else
                {
                    IsConnected = false;
                    StatusText = "Connection Failed";
                }

                StateChanged?.Invoke();
                return success;
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                IsConnected = false;
                StatusText = $"Connection Error: {ex.Message}";
                StateChanged?.Invoke();
                return false;
            }
        }

        public async Task<bool> DisconnectAsync()
        {
            try
            {
                IsConnecting = true;
                StatusText = "Disconnecting...";
                StateChanged?.Invoke();

                await CoreManager.StopAsync(Settings);

                IsConnected = false;
                IsConnecting = false;
                StatusText = "Disconnected";
                StateChanged?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                IsConnecting = false;
                IsConnected = false;
                StatusText = $"Disconnect Error: {ex.Message}";
                StateChanged?.Invoke();
                return false;
            }
        }

        public void SelectServer(VpnServerProfile server)
        {
            ActiveServer = server;
            Settings.ActiveServerId = server.Id;
            SaveSettings();
            StateChanged?.Invoke();
        }
    }
}
