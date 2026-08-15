using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyMapper
{
    public partial class VpnControlView : UserControl
    {
        private readonly VpnService _vpn = VpnService.Instance;
        private List<AppProcessItem> _scannedApps = new List<AppProcessItem>();
        private DateTime _connectStartTime = DateTime.MinValue;
        private System.Windows.Threading.DispatcherTimer? _durationTimer;

        public VpnControlView()
        {
            InitializeComponent();

            _vpn.StateChanged += () => Dispatcher.Invoke(RefreshUI);
            _vpn.Logs.CollectionChanged += (s, e) => Dispatcher.Invoke(UpdateLogsView);

            _durationTimer = new System.Windows.Threading.DispatcherTimer();
            _durationTimer.Interval = TimeSpan.FromSeconds(1);
            _durationTimer.Tick += (s, e) => UpdateDurationText();
            _durationTimer.Start();

            RefreshUI();
            LoadSettingsToUI();
            RefreshServersView();
        }

        private void UpdateDurationText()
        {
            if (_vpn.IsConnected)
            {
                if (_connectStartTime == DateTime.MinValue) _connectStartTime = DateTime.Now;
                var elapsed = DateTime.Now - _connectStartTime;
                HeroDurationText.Text = elapsed.ToString(@"hh\:mm\:ss");
            }
            else
            {
                _connectStartTime = DateTime.MinValue;
                HeroDurationText.Text = "00:00:00";
            }
        }

        private void VpnControlView_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                try
                {
                    string clipText = Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(clipText))
                    {
                        OpenAddModalWithText(clipText);
                        e.Handled = true;
                    }
                }
                catch { }
            }
        }

        private void RefreshUI()
        {
            if (_vpn.IsConnected)
            {
                HeroStatusText.Text = "CONNECTED";
                HeroStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
                HeroPowerBtn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981"));
            }
            else if (_vpn.IsConnecting)
            {
                HeroStatusText.Text = "CONNECTING...";
                HeroStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
                HeroPowerBtn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F59E0B"));
            }
            else
            {
                HeroStatusText.Text = "DISCONNECTED";
                HeroStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
                HeroPowerBtn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EF4444"));
            }

            if (_vpn.ActiveServer != null)
            {
                SelectedFlagText.Text = _vpn.ActiveServer.Flag;
                SelectedServerNameText.Text = _vpn.ActiveServer.Name;
            }
            else
            {
                SelectedFlagText.Text = "🌐";
                SelectedServerNameText.Text = "No Server Selected";
            }

            // Mode Pills (Happ Style)
            bool isTun = _vpn.Settings.ConnectionMode == "TUN";
            var accentBrush = (TryFindResource("AppAccentBrush") as Brush) ?? new SolidColorBrush(Color.FromRgb(56, 189, 248));
            var mutedBrush = (TryFindResource("AppMutedTextBrush") as Brush) ?? new SolidColorBrush(Color.FromRgb(148, 163, 184));

            ProxyModeChip.Background = !isTun ? accentBrush : Brushes.Transparent;
            ProxyModeChip.Foreground = !isTun ? Brushes.White : mutedBrush;

            TunModeChip.Background = isTun ? accentBrush : Brushes.Transparent;
            TunModeChip.Foreground = isTun ? Brushes.White : mutedBrush;
        }

        private void LoadSettingsToUI()
        {
            HttpPortBox.Text = _vpn.Settings.InboundHttpPort.ToString();
            SocksPortBox.Text = _vpn.Settings.InboundSocksPort.ToString();
            AllowLanCheckBox.IsChecked = _vpn.Settings.AllowLan;
            DnsServerBox.Text = _vpn.Settings.DnsServer;

            int perAppIndex = _vpn.Settings.PerAppMode switch
            {
                "Include" => 1,
                "Exclude" => 2,
                _ => 0
            };
            PerAppModeCombo.SelectedIndex = perAppIndex;
        }

        private void NavTab_Click(object sender, RoutedEventArgs e)
        {
            ServersMainView.Visibility = Visibility.Collapsed;
            SettingsMainView.Visibility = Visibility.Collapsed;
            PerAppMainView.Visibility = Visibility.Collapsed;
            LogsMainView.Visibility = Visibility.Collapsed;

            if (NavServersRadio.IsChecked == true) ServersMainView.Visibility = Visibility.Visible;
            else if (NavSettingsRadio.IsChecked == true) SettingsMainView.Visibility = Visibility.Visible;
            else if (NavPerAppRadio.IsChecked == true) PerAppMainView.Visibility = Visibility.Visible;
            else if (NavLogsRadio.IsChecked == true) LogsMainView.Visibility = Visibility.Visible;
        }

        private void ProxyModeChip_Click(object sender, RoutedEventArgs e)
        {
            _vpn.Settings.ConnectionMode = "SysProxy";
            _vpn.Settings.EnableSysProxy = true;
            _vpn.Settings.EnableTun = false;
            _vpn.SaveSettings();
            RefreshUI();
        }

        private void TunModeChip_Click(object sender, RoutedEventArgs e)
        {
            _vpn.Settings.ConnectionMode = "TUN";
            _vpn.Settings.EnableSysProxy = false;
            _vpn.Settings.EnableTun = true;
            _vpn.SaveSettings();
            RefreshUI();
        }

        public void FocusServerList()
        {
            NavServersRadio.IsChecked = true;
            NavTab_Click(this, new RoutedEventArgs());
            SearchBox.Focus();
        }

        public void FocusOverview()
        {
            NavServersRadio.IsChecked = true;
            NavTab_Click(this, new RoutedEventArgs());
        }

        private void RefreshServersView()
        {
            var search = SearchBox.Text?.Trim().ToLowerInvariant() ?? "";
            var activeId = _vpn.ActiveServer?.Id ?? "";

            foreach (var s in _vpn.Servers)
            {
                s.IsActive = (s.Id == activeId);
            }

            var groups = new List<VpnServerGroup>();

            // 1. Manual / Custom Nodes group (first at top)
            var manualNodes = _vpn.Servers.Where(s =>
                (string.Equals(s.SubscriptionName, "Manual", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(s.SubscriptionName)) &&
                (string.IsNullOrEmpty(search) || s.Name.ToLowerInvariant().Contains(search) || s.Address.ToLowerInvariant().Contains(search) || s.Protocol.ToLowerInvariant().Contains(search)))
                .ToList();

            if (manualNodes.Count > 0 || _vpn.Subscriptions.Count == 0)
            {
                groups.Add(new VpnServerGroup
                {
                    GroupName = "📌 Custom / Manual Nodes",
                    IsSubscription = false,
                    Subscription = null,
                    Servers = manualNodes
                });
            }

            // 2. Subscriptions groups
            foreach (var sub in _vpn.Subscriptions)
            {
                var subNodes = _vpn.Servers.Where(s =>
                    string.Equals(s.SubscriptionName, sub.Name, StringComparison.OrdinalIgnoreCase) &&
                    (string.IsNullOrEmpty(search) || s.Name.ToLowerInvariant().Contains(search) || s.Address.ToLowerInvariant().Contains(search) || s.Protocol.ToLowerInvariant().Contains(search)))
                    .ToList();

                groups.Add(new VpnServerGroup
                {
                    GroupName = $"🌐 {sub.Name}",
                    IsSubscription = true,
                    Subscription = sub,
                    Servers = subNodes
                });
            }

            GroupedServersItemsControl.ItemsSource = null;
            GroupedServersItemsControl.ItemsSource = groups;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshServersView();
        }

        private async void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            await _vpn.ToggleConnectionAsync();
        }

        private void ServerRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.DataContext is VpnServerProfile server)
            {
                _vpn.SelectServer(server);
                RefreshServersView();
                RefreshUI();
            }
        }

        private void DeleteServerBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnServerProfile server)
            {
                _vpn.Servers.Remove(server);
                _vpn.SaveServers();
                RefreshServersView();
                RefreshUI();
            }
        }

        private async void PingAllBtn_Click(object sender, RoutedEventArgs e)
        {
            TestPingBtnText.Text = "Testing...";

            var serversToTest = _vpn.Servers.ToList();
            var tasks = serversToTest.Select(server => Task.Run(async () =>
            {
                await SpeedTestService.TestServerAsync(server);
                Dispatcher.Invoke(RefreshServersView);
            }));

            await Task.WhenAll(tasks);

            _vpn.SaveServers();
            RefreshServersView();
            TestPingBtnText.Text = "Test ping";
        }

        private async void PingGroupBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnServerGroup group)
            {
                btn.IsEnabled = false;
                var serversToTest = group.Servers.ToList();
                var tasks = serversToTest.Select(server => Task.Run(async () =>
                {
                    await SpeedTestService.TestServerAsync(server);
                    Dispatcher.Invoke(RefreshServersView);
                }));

                await Task.WhenAll(tasks);

                _vpn.SaveServers();
                RefreshServersView();
                btn.IsEnabled = true;
            }
        }

        private void SidebarAddBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string clip = Clipboard.GetText();
                OpenAddModalWithText(clip);
            }
            catch
            {
                OpenAddModalWithText("");
            }
        }

        private void OpenAddModalWithText(string text)
        {
            ModalNameBox.Text = "";
            ModalUrlBox.Text = text ?? "";
            AddConfigModal.Visibility = Visibility.Visible;
        }

        private void CloseModal_Click(object sender, RoutedEventArgs e)
        {
            AddConfigModal.Visibility = Visibility.Collapsed;
        }

        private void ModalTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ModalNameLabel == null || ModalNameBox == null) return;
            bool isSub = ModalTypeCombo.SelectedIndex == 0;
            ModalNameLabel.Visibility = isSub ? Visibility.Visible : Visibility.Collapsed;
            ModalNameBox.Visibility = isSub ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void ConfirmModalAdd_Click(object sender, RoutedEventArgs e)
        {
            var text = ModalUrlBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            AddConfigModal.Visibility = Visibility.Collapsed;

            bool isSub = ModalTypeCombo.SelectedIndex == 0;
            if (isSub && Uri.TryCreate(text, UriKind.Absolute, out var uriResult))
            {
                string name = string.IsNullOrWhiteSpace(ModalNameBox.Text) ? $"Subscription {_vpn.Subscriptions.Count + 1}" : ModalNameBox.Text.Trim();
                var newSub = new VpnSubscription { Name = name, Url = text };
                _vpn.Subscriptions.Insert(0, newSub);
                _vpn.SaveSubscriptions();

                var (nodes, updatedSub) = await SubscriptionManagerService.FetchSubscriptionAsync(newSub);
                if (nodes.Count > 0)
                {
                    var toRemove = _vpn.Servers.Where(s => s.SubscriptionName.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var item in toRemove) _vpn.Servers.Remove(item);
                    foreach (var n in nodes) _vpn.Servers.Add(n);
                    _vpn.SaveServers();
                    _vpn.SaveSubscriptions();
                }

                RefreshServersView();
                MessageBox.Show($"Added subscription '{name}' with {nodes.Count} nodes!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                var parsedNodes = LinkParserService.ParseContent(text, "Manual");
                if (parsedNodes.Count > 0)
                {
                    foreach (var node in parsedNodes) _vpn.Servers.Insert(0, node);
                    _vpn.SaveServers();
                    RefreshServersView();
                    MessageBox.Show($"Imported {parsedNodes.Count} node(s)!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("No valid proxy nodes found.", "Import Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private async void RefreshGroupSubBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnServerGroup group && group.Subscription != null)
            {
                var (nodes, subInfo) = await SubscriptionManagerService.FetchSubscriptionAsync(group.Subscription);
                var toRemove = _vpn.Servers.Where(s => s.SubscriptionName.Equals(group.Subscription.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                foreach (var item in toRemove) _vpn.Servers.Remove(item);
                foreach (var n in nodes) _vpn.Servers.Add(n);

                _vpn.SaveSubscriptions();
                _vpn.SaveServers();
                RefreshServersView();
            }
        }

        private void DeleteGroupSubBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is VpnServerGroup group && group.Subscription != null)
            {
                if (MessageBox.Show($"Delete subscription '{group.Subscription.Name}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                {
                    var toRemove = _vpn.Servers.Where(s => s.SubscriptionName.Equals(group.Subscription.Name, StringComparison.OrdinalIgnoreCase)).ToList();
                    foreach (var item in toRemove) _vpn.Servers.Remove(item);
                    _vpn.Subscriptions.Remove(group.Subscription);

                    _vpn.SaveSubscriptions();
                    _vpn.SaveServers();
                    RefreshServersView();
                }
            }
        }

        private void ClearOfflineBtn_Click(object sender, RoutedEventArgs e)
        {
            var offline = _vpn.Servers.Where(s => s.PingMs >= 9999).ToList();
            foreach (var s in offline) _vpn.Servers.Remove(s);
            _vpn.SaveServers();
            RefreshServersView();
        }

        private void RefreshAppsBtn_Click(object sender, RoutedEventArgs e)
        {
            _scannedApps = ProcessScannerService.GetRunningApplications();
            foreach (var app in _scannedApps)
            {
                if (_vpn.Settings.SelectedPerAppProcesses.Contains(app.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    app.IsSelected = true;
                }
            }
            AppsListView.ItemsSource = _scannedApps;
        }

        private void AppCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _vpn.Settings.SelectedPerAppProcesses = _scannedApps
                .Where(a => a.IsSelected)
                .Select(a => a.ProcessName)
                .ToList();
            _vpn.SaveSettings();
        }

        private void PerAppModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PerAppModeCombo == null) return;
            _vpn.Settings.PerAppMode = PerAppModeCombo.SelectedIndex switch
            {
                1 => "Include",
                2 => "Exclude",
                _ => "Disabled"
            };
            _vpn.SaveSettings();
        }

        private async void DownloadCoreBtn_Click(object sender, RoutedEventArgs e)
        {
            var success = await CoreDownloaderService.DownloadSingBoxCoreAsync(msg =>
            {
                Dispatcher.Invoke(() => _vpn.Logs.Add(msg));
            });
            if (success)
            {
                MessageBox.Show("Sing-Box core binary downloaded successfully!", "VPN Core", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to download Sing-Box core binary.", "VPN Core Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void HttpPortBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(HttpPortBox.Text, out int port))
            {
                _vpn.Settings.InboundHttpPort = port;
                _vpn.SaveSettings();
            }
        }

        private void SocksPortBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(SocksPortBox.Text, out int port))
            {
                _vpn.Settings.InboundSocksPort = port;
                _vpn.SaveSettings();
            }
        }

        private void AllowLanCheckBox_Click(object sender, RoutedEventArgs e)
        {
            _vpn.Settings.AllowLan = AllowLanCheckBox.IsChecked == true;
            _vpn.SaveSettings();
        }

        private void DnsServerBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _vpn.Settings.DnsServer = DnsServerBox.Text?.Trim() ?? "8.8.8.8";
            _vpn.SaveSettings();
        }

        private void UpdateLogsView()
        {
            if (LogsBox == null) return;
            LogsBox.Text = string.Join(Environment.NewLine, _vpn.Logs);
            LogsScrollViewer?.ScrollToEnd();
        }

        private void ClearLogsBtn_Click(object sender, RoutedEventArgs e)
        {
            _vpn.Logs.Clear();
            UpdateLogsView();
        }

        private void ToggleOverlayWidget_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ToggleVpnOverlayWidget();
            }
        }
    }
}
