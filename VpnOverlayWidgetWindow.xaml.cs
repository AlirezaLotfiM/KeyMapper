using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class VpnOverlayWidgetWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hwnd, int index);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly VpnService _vpn = VpnService.Instance;
        private readonly DispatcherTimer _connectingAnimationTimer;
        private readonly DispatcherTimer _trafficTimer;

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _connectingAngle = 0;

        public VpnOverlayWidgetWindow()
        {
            InitializeComponent();

            _connectingAnimationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            _connectingAnimationTimer.Tick += ConnectingAnimationTimer_Tick;

            _trafficTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _trafficTimer.Tick += (s, e) => UpdateTrafficStats();
            _trafficTimer.Start();

            _vpn.StateChanged += () => Dispatcher.Invoke(RefreshUI);
            _vpn.Servers.CollectionChanged += (s, e) => Dispatcher.Invoke(RefreshServers);
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                var helper = new System.Windows.Interop.WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);
                SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RestorePosition();
            RefreshUI();
            RefreshServers();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SavePosition();
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (ServerFlyoutPopup.IsOpen && !IsMouseOver)
            {
                ServerFlyoutPopup.IsOpen = false;
            }
        }

        private void RestorePosition()
        {
            var settings = ConfigManager.Load();
            double screenWidth = SystemParameters.WorkArea.Width;
            double screenHeight = SystemParameters.WorkArea.Height;
            double screenLeft = SystemParameters.WorkArea.Left;
            double screenTop = SystemParameters.WorkArea.Top;

            if (settings.VpnOverlayLeft.HasValue && settings.VpnOverlayTop.HasValue)
            {
                double left = settings.VpnOverlayLeft.Value;
                double top = settings.VpnOverlayTop.Value;

                left = Math.Max(screenLeft + 10, Math.Min(left, screenLeft + screenWidth - 85));
                top = Math.Max(screenTop + 10, Math.Min(top, screenTop + screenHeight - 85));

                Left = left;
                Top = top;
            }
            else
            {
                Left = screenLeft + screenWidth - 95;
                Top = screenTop + screenHeight - 110;
            }
        }

        private void SavePosition()
        {
            try
            {
                var settings = ConfigManager.Load();
                settings.VpnOverlayLeft = Left;
                settings.VpnOverlayTop = Top;
                ConfigManager.Save(settings);
            }
            catch { }
        }

        public void RefreshUI()
        {
            if (_vpn.IsConnected)
            {
                _connectingAnimationTimer.Stop();
                OrbConnectingRing.Visibility = Visibility.Collapsed;

                var greenBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                OrbContainer.BorderBrush = greenBrush;
                OrbContainer.Background = new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
                OrbPowerIcon.Fill = greenBrush;

                FlyoutStatusDot.Fill = greenBrush;
                FlyoutStatusText.Text = "CONNECTED";
                FlyoutStatusText.Foreground = greenBrush;
            }
            else if (_vpn.IsConnecting)
            {
                if (!_connectingAnimationTimer.IsEnabled) _connectingAnimationTimer.Start();
                OrbConnectingRing.Visibility = Visibility.Visible;

                var amberBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
                OrbContainer.BorderBrush = amberBrush;
                OrbContainer.Background = new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
                OrbPowerIcon.Fill = amberBrush;

                FlyoutStatusDot.Fill = amberBrush;
                FlyoutStatusText.Text = "CONNECTING...";
                FlyoutStatusText.Foreground = amberBrush;
            }
            else
            {
                _connectingAnimationTimer.Stop();
                OrbConnectingRing.Visibility = Visibility.Collapsed;

                var redBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                OrbContainer.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 239, 68, 68));
                OrbContainer.Background = new SolidColorBrush(Color.FromArgb(35, 14, 19, 32));
                OrbPowerIcon.Fill = redBrush;

                FlyoutStatusDot.Fill = redBrush;
                FlyoutStatusText.Text = "DISCONNECTED";
                FlyoutStatusText.Foreground = redBrush;
            }

            if (_vpn.ActiveServer != null)
            {
                OrbFlagText.Text = _vpn.ActiveServer.CountryCodeDisplay;
                FlyoutActiveCountryText.Text = _vpn.ActiveServer.CountryCodeDisplay;
                FlyoutActiveServerText.Text = _vpn.ActiveServer.Name;
            }
            else
            {
                OrbFlagText.Text = "UN";
                FlyoutActiveCountryText.Text = "UN";
                FlyoutActiveServerText.Text = "No Server Selected";
            }

            UpdateTrafficStats();
        }

        private void UpdateTrafficStats()
        {
            if (_vpn.IsConnected && _vpn.Traffic != null)
            {
                FlyoutUpSpeedText.Text = _vpn.Traffic.UploadSpeedDisplay;
                FlyoutDownSpeedText.Text = _vpn.Traffic.DownloadSpeedDisplay;
            }
            else
            {
                FlyoutUpSpeedText.Text = "0 B/s";
                FlyoutDownSpeedText.Text = "0 B/s";
            }
        }

        public void RefreshServers()
        {
            var list = _vpn.Servers.ToList();
            ServersItemsControl.ItemsSource = list;
            FlyoutServerCountText.Text = $"{list.Count} Servers";
        }

        private void ConnectingAnimationTimer_Tick(object? sender, EventArgs e)
        {
            _connectingAngle = (_connectingAngle + 12) % 360;
            ConnectingRotate.Angle = _connectingAngle;
        }

        #region Hover & Visibility

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(180));
            RootGrid.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!ServerFlyoutPopup.IsOpen)
            {
                var anim = new DoubleAnimation(0.40, TimeSpan.FromMilliseconds(250));
                RootGrid.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        #endregion

        #region Flyout Drawer (Popup)

        private void ToggleFlyout()
        {
            if (ServerFlyoutPopup.IsOpen)
            {
                ServerFlyoutPopup.IsOpen = false;
            }
            else
            {
                double screenLeft = SystemParameters.WorkArea.Left;
                double screenWidth = SystemParameters.WorkArea.Width;

                if (Left < screenLeft + (screenWidth / 2))
                {
                    ServerFlyoutPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Right;
                    ServerFlyoutPopup.HorizontalOffset = 8;
                }
                else
                {
                    ServerFlyoutPopup.Placement = System.Windows.Controls.Primitives.PlacementMode.Left;
                    ServerFlyoutPopup.HorizontalOffset = -8;
                }

                RefreshServers();
                ServerFlyoutPopup.IsOpen = true;
            }
        }

        private void ServerFlyoutPopup_Opened(object? sender, EventArgs e)
        {
            RootGrid.Opacity = 1.0;
        }

        private void ServerFlyoutPopup_Closed(object? sender, EventArgs e)
        {
            if (!IsMouseOver)
            {
                var fadeAnim = new DoubleAnimation(0.40, TimeSpan.FromMilliseconds(200));
                RootGrid.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }
        }

        private void CloseFlyout_Click(object sender, RoutedEventArgs e)
        {
            ServerFlyoutPopup.IsOpen = false;
        }

        #endregion

        #region Drag & Click

        private void Orb_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _dragStartPoint = e.GetPosition(this);
        }

        private void Orb_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                var currentPoint = e.GetPosition(this);
                if (Math.Abs(currentPoint.X - _dragStartPoint.X) > 4 || Math.Abs(currentPoint.Y - _dragStartPoint.Y) > 4)
                {
                    _isDragging = true;
                    if (ServerFlyoutPopup.IsOpen) ServerFlyoutPopup.IsOpen = false;
                    try
                    {
                        DragMove();
                        SavePosition();
                    }
                    catch { }
                }
            }
        }

        private async void Orb_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                SavePosition();
                e.Handled = true;
                return;
            }

            await _vpn.ToggleConnectionAsync();
        }

        private void Orb_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ToggleFlyout();
            e.Handled = true;
        }

        #endregion

        #region Server Selection & Actions

        private async void ServerRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement el && el.Tag is VpnServerProfile server)
            {
                _vpn.SelectServer(server);
                RefreshUI();

                await _vpn.ConnectAsync(server);
            }
        }

        private async void PingAll_Click(object sender, RoutedEventArgs e)
        {
            PingAllBtn.IsEnabled = false;
            PingAllBtn.Opacity = 0.5;

            var serversToTest = _vpn.Servers.ToList();
            var tasks = serversToTest.Select(server => Task.Run(async () =>
            {
                await SpeedTestService.TestServerAsync(server);
            }));

            await Task.WhenAll(tasks);

            _vpn.SaveServers();
            RefreshServers();
            PingAllBtn.Opacity = 1.0;
            PingAllBtn.IsEnabled = true;
        }

        private async void UpdateSubs_Click(object sender, RoutedEventArgs e)
        {
            UpdateSubsBtn.IsEnabled = false;
            UpdateSubsBtn.Opacity = 0.5;

            await SubscriptionManagerService.UpdateAllSubscriptionsAsync();

            RefreshServers();
            RefreshUI();

            UpdateSubsBtn.Opacity = 1.0;
            UpdateSubsBtn.IsEnabled = true;
        }

        private void OpenVpnManager_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.Show();
                mainWin.WindowState = WindowState.Normal;
                mainWin.Activate();
                mainWin.OpenVpnTab(focusServerSelection: false);
            }
        }

        #endregion
    }
}
