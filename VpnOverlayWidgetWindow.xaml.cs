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
        private readonly DispatcherTimer _trafficTimer;

        private bool _isDragging = false;
        private Point _dragStartPoint;
        private double _dragStartTop;

        public VpnOverlayWidgetWindow()
        {
            InitializeComponent();

            _trafficTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _trafficTimer.Tick += (s, e) => UpdateTrafficStats();
            _trafficTimer.Start();

            // VPN Service events
            _vpn.StateChanged += () => Dispatcher.Invoke(RefreshUI);
            _vpn.Servers.CollectionChanged += (s, e) => Dispatcher.Invoke(RefreshServers);

            // Music Service events
            try
            {
                LocalAudioPlayerService.Instance.OnTrackChanged += track => Dispatcher.Invoke(RefreshMusicUI);
                LocalAudioPlayerService.Instance.OnPlaybackStateChanged += isPlaying => Dispatcher.Invoke(RefreshMusicUI);
            }
            catch { }
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
            RefreshMusicUI();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SavePosition();
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            if (!IsMouseOver && (Level1Panel.Visibility == Visibility.Visible || Level2Panel.Visibility == Visibility.Visible))
            {
                CloseAllPanels();
            }
        }

        #region Positioning (Screen Right Edge Glued)

        private void RestorePosition()
        {
            var settings = ConfigManager.Load();
            double screenHeight = SystemParameters.WorkArea.Height;
            double screenTop = SystemParameters.WorkArea.Top;
            double screenRight = SystemParameters.WorkArea.Right;

            // X is always flush against the right screen edge
            Left = screenRight - Width;

            if (settings.VpnOverlayTop.HasValue)
            {
                double top = settings.VpnOverlayTop.Value;
                top = Math.Max(screenTop + 10, Math.Min(top, screenTop + screenHeight - Height - 10));
                Top = top;
            }
            else
            {
                Top = screenTop + (screenHeight / 2) - (Height / 2);
            }
        }

        private void SavePosition()
        {
            try
            {
                var settings = ConfigManager.Load();
                settings.VpnOverlayTop = Top;
                ConfigManager.Save(settings);
            }
            catch { }
        }

        #endregion

        #region Edge Handle Dragging & Panel Toggling

        private void EdgeHandle_MouseEnter(object sender, MouseEventArgs e)
        {
            var anim = new DoubleAnimation(0.95, TimeSpan.FromMilliseconds(150));
            EdgeHandle.BeginAnimation(UIElement.OpacityProperty, anim);
        }

        private void EdgeHandle_MouseLeave(object sender, MouseEventArgs e)
        {
            if (Level1Panel.Visibility != Visibility.Visible && Level2Panel.Visibility != Visibility.Visible && !_isDragging)
            {
                var anim = new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(250));
                EdgeHandle.BeginAnimation(UIElement.OpacityProperty, anim);
            }
        }

        private void EdgeHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            _dragStartPoint = e.GetPosition(this);
            _dragStartTop = Top;
            EdgeHandle.CaptureMouse();
        }

        private void EdgeHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (EdgeHandle.IsMouseCaptured)
            {
                var currentPoint = e.GetPosition(this);
                double deltaY = currentPoint.Y - _dragStartPoint.Y;

                if (Math.Abs(deltaY) > 3)
                {
                    _isDragging = true;
                    if (Level1Panel.Visibility == Visibility.Visible || Level2Panel.Visibility == Visibility.Visible)
                    {
                        CloseAllPanels();
                    }

                    double screenTop = SystemParameters.WorkArea.Top;
                    double screenHeight = SystemParameters.WorkArea.Height;
                    double newTop = _dragStartTop + deltaY;
                    newTop = Math.Max(screenTop + 10, Math.Min(newTop, screenTop + screenHeight - Height - 10));

                    Top = newTop;
                    SavePosition();
                }
            }
        }

        private void EdgeHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            EdgeHandle.ReleaseMouseCapture();

            if (_isDragging)
            {
                _isDragging = false;
                SavePosition();
                e.Handled = true;
                return;
            }

            // Click -> Toggle Level 1 Edge Panel
            ToggleLevel1Panel();
        }

        private void EdgeHandle_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            ToggleLevel1Panel();
            e.Handled = true;
        }

        private void ToggleLevel1Panel()
        {
            if (Level1Panel.Visibility == Visibility.Visible || Level2Panel.Visibility == Visibility.Visible)
            {
                CloseAllPanels();
            }
            else
            {
                OpenLevel1Panel();
            }
        }

        private void OpenLevel1Panel()
        {
            Level2Panel.Visibility = Visibility.Collapsed;
            Level1Panel.Visibility = Visibility.Visible;
            EdgeHandle.Opacity = 0.95;
            RefreshUI();
            RefreshMusicUI();
        }

        private void CloseAllPanels()
        {
            Level1Panel.Visibility = Visibility.Collapsed;
            Level2Panel.Visibility = Visibility.Collapsed;
            if (!IsMouseOver)
            {
                var fadeAnim = new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(200));
                EdgeHandle.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }
        }

        private void ClosePanel_Click(object sender, RoutedEventArgs e)
        {
            CloseAllPanels();
        }

        #endregion

        #region Level 2 Navigation & Transitions (Smooth Slide-In)

        private void OpenVpnServersLevel2_Click(object sender, RoutedEventArgs e)
        {
            Level1Panel.Visibility = Visibility.Collapsed;
            Level2Panel.Visibility = Visibility.Visible;

            RefreshServers();
            RefreshUI();

            // Smooth Slide-in Animation from Right (280 -> 0)
            var slideAnim = new DoubleAnimation
            {
                From = 280,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            Level2SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        }

        private void BackToLevel1_Click(object sender, RoutedEventArgs e)
        {
            // Smooth Slide-out Animation to Right (0 -> 280)
            var slideAnim = new DoubleAnimation
            {
                From = 0,
                To = 280,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            slideAnim.Completed += (s, ev) =>
            {
                Level2Panel.Visibility = Visibility.Collapsed;
                Level1Panel.Visibility = Visibility.Visible;
            };
            Level2SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
        }

        #endregion

        #region Module 1: VPN Actions

        public void RefreshUI()
        {
            if (_vpn.IsConnected)
            {
                var greenBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
                var redBrush = new SolidColorBrush(Color.FromRgb(239, 68, 68));

                VpnLevel1ToggleBtn.Background = redBrush;
                VpnLevel1BtnText.Text = "Disconnect";

                VpnLevel2ToggleBtn.Background = redBrush;
                VpnLevel2BtnText.Text = $"Disconnect ({_vpn.ActiveServer?.CountryCodeDisplay ?? "VPN"})";

                EdgeHandle.Background = new SolidColorBrush(Color.FromArgb(160, 16, 185, 129));
            }
            else if (_vpn.IsConnecting)
            {
                var amberBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));

                VpnLevel1ToggleBtn.Background = amberBrush;
                VpnLevel1BtnText.Text = "Connecting...";

                VpnLevel2ToggleBtn.Background = amberBrush;
                VpnLevel2BtnText.Text = "Connecting...";

                EdgeHandle.Background = new SolidColorBrush(Color.FromArgb(160, 245, 158, 11));
            }
            else
            {
                var skyBrush = new SolidColorBrush(Color.FromRgb(14, 165, 233));

                VpnLevel1ToggleBtn.Background = skyBrush;
                VpnLevel1BtnText.Text = "Connect";

                VpnLevel2ToggleBtn.Background = skyBrush;
                VpnLevel2BtnText.Text = "Connect to Selected";

                EdgeHandle.Background = new SolidColorBrush(Color.FromArgb(100, 255, 255, 255));
            }

            UpdateTrafficStats();
        }

        private void UpdateTrafficStats()
        {
            if (_vpn.IsConnected && _vpn.Traffic != null)
            {
                L2TrafficText.Text = $"▲ {_vpn.Traffic.UploadSpeedDisplay}   •   ▼ {_vpn.Traffic.DownloadSpeedDisplay}";
            }
            else
            {
                L2TrafficText.Text = "▲ 0 B/s   •   ▼ 0 B/s";
            }
        }

        public void RefreshServers()
        {
            var list = _vpn.Servers.ToList();
            ServersItemsControl.ItemsSource = list;
        }

        private async void VpnConnectToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_vpn.IsConnected || _vpn.IsConnecting)
            {
                await _vpn.DisconnectAsync();
            }
            else
            {
                var target = _vpn.ActiveServer ?? _vpn.Servers.FirstOrDefault();
                if (target != null)
                {
                    await _vpn.ConnectAsync(target);
                }
            }
            RefreshUI();
        }

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
            var serversToTest = _vpn.Servers.ToList();
            var tasks = serversToTest.Select(server => Task.Run(async () =>
            {
                await SpeedTestService.TestServerAsync(server);
            }));

            await Task.WhenAll(tasks);

            _vpn.SaveServers();
            RefreshServers();
        }

        private async void UpdateSubs_Click(object sender, RoutedEventArgs e)
        {
            await SubscriptionManagerService.UpdateAllSubscriptionsAsync();
            RefreshServers();
            RefreshUI();
        }

        #endregion

        #region Module 2: Music Player Actions

        private void RefreshMusicUI()
        {
            try
            {
                var player = LocalAudioPlayerService.Instance;
                var track = player.CurrentTrack;

                if (track != null)
                {
                    L1MusicCoverImg.Source = track.AlbumArt;
                    L1MusicTitleText.Text = track.DisplayTitle;
                }
                else
                {
                    L1MusicCoverImg.Source = null;
                    L1MusicTitleText.Text = "No Track Playing";
                }

                L1MusicPlayPauseText.Text = player.IsPlaying ? "⏸ Pause" : "▶ Play";
            }
            catch { }
        }

        private void MusicPlayPause_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.TogglePlayPause();
            RefreshMusicUI();
        }

        private void MusicPrev_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayPrevious();
            RefreshMusicUI();
        }

        private void MusicNext_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayNext();
            RefreshMusicUI();
        }

        #endregion

        #region Module 3: Fence Actions

        private void CreateNewFence_Click(object sender, RoutedEventArgs e)
        {
            DesktopFenceManager.Instance.CreateNewFence();
        }

        private void CreatePortalFence_Click(object sender, RoutedEventArgs e)
        {
            string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            DesktopFenceManager.Instance.CreateFolderPortalFence("Desktop Portal", desktopPath);
        }

        #endregion

        #region Module 4: Sticky Note Actions

        private void CreateNewNote_Click(object sender, RoutedEventArgs e)
        {
            StickyNoteManager.Instance.CreateNewNote("Quick Note", "");
        }

        private void ShowAllNotes_Click(object sender, RoutedEventArgs e)
        {
            var noteWindows = Application.Current.Windows.OfType<StickyNoteWindow>().ToList();
            if (noteWindows.Count > 0)
            {
                foreach (var win in noteWindows)
                {
                    win.Show();
                    win.WindowState = WindowState.Normal;
                }
            }
            else
            {
                foreach (var note in StickyNoteManager.Instance.Notes)
                {
                    StickyNoteManager.Instance.OpenNoteWindow(note);
                }
            }
        }

        private void HideAllNotes_Click(object sender, RoutedEventArgs e)
        {
            var noteWindows = Application.Current.Windows.OfType<StickyNoteWindow>().ToList();
            foreach (var win in noteWindows)
            {
                win.Hide();
            }
        }

        #endregion
    }
}
