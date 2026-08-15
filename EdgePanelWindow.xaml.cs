using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class EdgePanelWindow : Window
    {
        private enum PanelState
        {
            Collapsed,
            Level1,
            Level2
        }

        private PanelState _currentState = PanelState.Collapsed;
        private bool _isDraggingHandle;
        private Point _handleDragStartScreen;
        private double _handleStartTop;
        private readonly DispatcherTimer _trafficTimer;
        private bool _isPingingAll = false;
        private bool _isUpdatingSubs = false;
        private bool _isDraggingSlider = false;
        private bool _syncingVolume = false;

        public EdgePanelWindow()
        {
            InitializeComponent();

            _trafficTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _trafficTimer.Tick += (s, e) => UpdateTrafficUI();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindowOnEdge();

            // Load position from settings if saved
            var settings = ConfigManager.Load();
            if (settings.EdgePanelTop.HasValue)
            {
                double top = settings.EdgePanelTop.Value;
                double maxTop = SystemParameters.WorkArea.Bottom - Height;
                if (top >= SystemParameters.WorkArea.Top && top <= maxTop)
                {
                    Top = top;
                }
            }

            // Hook up VPN events
            VpnService.Instance.StateChanged += OnVpnStateChanged;
            VpnService.Instance.CoreManager.TrafficUpdated += OnVpnTrafficUpdated;

            // Hook up Music Player events
            LocalAudioPlayerService.Instance.OnTrackChanged += OnMusicTrackChanged;
            LocalAudioPlayerService.Instance.OnPlaybackStateChanged += OnMusicPlaybackStateChanged;
            LocalAudioPlayerService.Instance.OnPositionChanged += OnMusicPositionChanged;
            LocalAudioPlayerService.Instance.OnVolumeChanged += OnMusicVolumeChanged;
            LocalAudioPlayerService.Instance.OnFavoritesUpdated += OnMusicFavoritesUpdated;

            // Initialize UI States
            RefreshVpnUI();
            RefreshMusicUI();
            RefreshServersList();

            // Initial library scan if not loaded yet
            if (LocalAudioPlayerService.Instance.Playlist.Count == 0)
            {
                _ = LocalAudioPlayerService.Instance.ScanLibraryAsync();
            }

            _trafficTimer.Start();
        }

        private void PositionWindowOnEdge()
        {
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width;
            if (Top < workArea.Top || Top > workArea.Bottom - Height)
            {
                Top = workArea.Top + (workArea.Height - Height) / 2.0;
            }
        }

        private void SavePosition()
        {
            try
            {
                var settings = ConfigManager.Load();
                settings.EdgePanelTop = Top;
                ConfigManager.Save(settings);
            }
            catch { }
        }

        #region State & Animation Transitions

        public void TogglePanel()
        {
            if (_currentState == PanelState.Collapsed)
            {
                OpenLevel1();
            }
            else
            {
                CollapsePanel();
            }
        }

        public void OpenLevel1()
        {
            _currentState = PanelState.Level1;
            EdgeHandle.Visibility = Visibility.Collapsed;
            Level1SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            Level1Panel.BeginAnimation(UIElement.OpacityProperty, null);
            Level2Panel.Visibility = Visibility.Collapsed;
            Level1Panel.Visibility = Visibility.Visible;

            PositionWindowOnEdge();
            UpdateLayout();
            Level1SlideTransform.X = 250;
            Level1Panel.Opacity = 0;

            var anim = new DoubleAnimation
            {
                From = 250,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(280),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(220)
            };

            Level1SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
            Level1Panel.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

            RefreshVpnUI();
            RefreshMusicUI();
        }

        public void OpenLevel2()
        {
            _currentState = PanelState.Level2;
            Level2SlideTransform.BeginAnimation(TranslateTransform.XProperty, null);
            Level2Panel.BeginAnimation(UIElement.OpacityProperty, null);
            Level2Panel.Visibility = Visibility.Visible;
            UpdateLayout();
            Level2SlideTransform.X = 360;
            Level2Panel.Opacity = 0;

            RefreshServersList();

            var anim = new DoubleAnimation
            {
                From = 360,
                To = 0,
                Duration = TimeSpan.FromMilliseconds(320),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var opacityAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(240)
            };

            Level2SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
            Level2Panel.BeginAnimation(UIElement.OpacityProperty, opacityAnim);
        }

        public void BackToLevel1()
        {
            if (_currentState != PanelState.Level2) return;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = 360,
                Duration = TimeSpan.FromMilliseconds(240),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            anim.Completed += (s, e) =>
            {
                Level2Panel.Visibility = Visibility.Collapsed;
                _currentState = PanelState.Level1;
                Level1Panel.Visibility = Visibility.Visible;
            };

            Level2SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        public void CollapsePanel()
        {
            if (_currentState == PanelState.Collapsed) return;

            var prev = _currentState;
            _currentState = PanelState.Collapsed;

            var anim = new DoubleAnimation
            {
                From = 0,
                To = prev == PanelState.Level2 ? 360 : 250,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            anim.Completed += (s, e) =>
            {
                Level1Panel.Visibility = Visibility.Collapsed;
                Level2Panel.Visibility = Visibility.Collapsed;
                EdgeHandle.Visibility = Visibility.Visible;
            };

            if (prev == PanelState.Level2)
            {
                Level2SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
            }
            else
            {
                Level1SlideTransform.BeginAnimation(TranslateTransform.XProperty, anim);
            }
        }

        #endregion

        #region Handle Interaction

        private void EdgeHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingHandle = false;
            _handleDragStartScreen = PointToScreen(e.GetPosition(this));
            _handleStartTop = Top;
            EdgeHandle.CaptureMouse();
        }

        private void EdgeHandle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!EdgeHandle.IsMouseCaptured)
            {
                return;
            }

            Point currentScreen = PointToScreen(e.GetPosition(this));
            double deltaY = currentScreen.Y - _handleDragStartScreen.Y;

            if (Math.Abs(deltaY) > 5)
            {
                _isDraggingHandle = true;
            }

            if (_isDraggingHandle)
            {
                double minTop = SystemParameters.WorkArea.Top;
                double maxTop = SystemParameters.WorkArea.Bottom - Height;
                Top = Math.Clamp(_handleStartTop + deltaY, minTop, maxTop);
            }
        }

        private void EdgeHandle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!EdgeHandle.IsMouseCaptured)
            {
                return;
            }

            EdgeHandle.ReleaseMouseCapture();

            if (_isDraggingHandle)
            {
                SavePosition();
            }
            else
            {
                TogglePanel();
            }

            _isDraggingHandle = false;
            e.Handled = true;
        }

        #endregion

        #region VPN Module & Level 2 Handlers

        private Storyboard? _vpnConnectingStoryboard;
        private Storyboard? _vpnConnectedStoryboard;

        private void OnVpnStateChanged()
        {
            Dispatcher.Invoke(() =>
            {
                RefreshVpnUI();
                RefreshServersList();
            });
        }

        private void OnVpnTrafficUpdated(TrafficStats stats)
        {
            Dispatcher.Invoke(() =>
            {
                UpdateTrafficUI();
            });
        }

        private void RefreshVpnUI()
        {
            var vpn = VpnService.Instance;
            L2SelectedServerText.Text = vpn.ActiveServer?.CleanName ?? "No server selected";
            L1VpnFlagText.Text = vpn.ActiveServer?.Flag ?? "🌐";

            _vpnConnectingStoryboard ??= TryFindResource("VpnConnectingStoryboard") as Storyboard;
            _vpnConnectedStoryboard ??= TryFindResource("VpnConnectedGlowStoryboard") as Storyboard;

            if (vpn.IsConnected)
            {
                _vpnConnectingStoryboard?.Stop(this);
                _vpnConnectedStoryboard?.Begin(this, true);

                SetVpnPowerTheme(
                    mainColor: Color.FromRgb(34, 197, 94),     // #22C55E
                    accentColor: Color.FromRgb(74, 222, 128),  // #4ADE80
                    glowColor: Color.FromRgb(34, 197, 94),
                    isConnecting: false);

                L1VpnStatusBadge.Text = "ACTIVE";
                L1VpnStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                L1VpnStatusText.Text = "Connected";
                L1VpnServerName.Text = vpn.ActiveServer?.Name ?? "Active Server";
                VpnLevel1BtnText.Text = "Disconnect";
                VpnLevel1BtnText.Foreground = new SolidColorBrush(Color.FromRgb(187, 247, 208));

                VpnLevel2BtnText.Text = "Disconnect VPN";
                L2VpnPowerIcon.Foreground = new SolidColorBrush(Color.FromRgb(74, 222, 128));
                L2VpnSpinnerRing.Visibility = Visibility.Collapsed;
            }
            else if (vpn.IsConnecting)
            {
                _vpnConnectedStoryboard?.Stop(this);
                _vpnConnectingStoryboard?.Begin(this, true);

                SetVpnPowerTheme(
                    mainColor: Color.FromRgb(245, 158, 11),    // #F59E0B
                    accentColor: Color.FromRgb(251, 191, 36),  // #FBBF24
                    glowColor: Color.FromRgb(245, 158, 11),
                    isConnecting: true);

                L1VpnStatusBadge.Text = "CONNECTING";
                L1VpnStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                L1VpnStatusText.Text = "Connecting...";
                L1VpnServerName.Text = vpn.ActiveServer?.Name ?? "Selected Server";
                VpnLevel1BtnText.Text = "Connecting…";
                VpnLevel1BtnText.Foreground = new SolidColorBrush(Color.FromRgb(254, 240, 138));

                VpnLevel2BtnText.Text = "Connecting…";
                L2VpnPowerIcon.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
                L2VpnSpinnerRing.Visibility = Visibility.Visible;
            }
            else
            {
                _vpnConnectingStoryboard?.Stop(this);
                _vpnConnectedStoryboard?.Stop(this);

                // Reset transforms
                L1VpnOuterScale.ScaleX = 1.0;
                L1VpnOuterScale.ScaleY = 1.0;
                L1VpnPowerScale.ScaleX = 1.0;
                L1VpnPowerScale.ScaleY = 1.0;
                L1VpnPowerOuterRing.Opacity = 0.5;

                SetVpnPowerTheme(
                    mainColor: Color.FromRgb(251, 113, 133),   // #FB7185
                    accentColor: Color.FromRgb(244, 63, 94),   // #F43F5E
                    glowColor: Color.FromRgb(251, 113, 133),
                    isConnecting: false);

                L1VpnStatusBadge.Text = "OFF";
                L1VpnStatusBadge.Foreground = new SolidColorBrush(Color.FromRgb(251, 113, 133));
                L1VpnStatusText.Text = "Disconnected";
                L1VpnServerName.Text = vpn.ActiveServer != null ? $"Ready: {vpn.ActiveServer.Name}" : "No Server Selected";
                VpnLevel1BtnText.Text = "Connect";
                VpnLevel1BtnText.Foreground = new SolidColorBrush(Color.FromRgb(233, 251, 255));

                VpnLevel2BtnText.Text = vpn.ActiveServer != null ? $"Connect: {vpn.ActiveServer.Name}" : "Connect VPN";
                L2VpnPowerIcon.Foreground = Brushes.White;
                L2VpnSpinnerRing.Visibility = Visibility.Collapsed;
            }
        }

        private void SetVpnPowerTheme(Color mainColor, Color accentColor, Color glowColor, bool isConnecting)
        {
            var mainBrush = new SolidColorBrush(mainColor);
            var accentBrush = new SolidColorBrush(accentColor);
            var discBrush = new SolidColorBrush(Color.FromArgb(40, mainColor.R, mainColor.G, mainColor.B));

            L1VpnPowerOuterRing.Stroke = mainBrush;
            L1VpnPowerOuterRing.Visibility = isConnecting ? Visibility.Collapsed : Visibility.Visible;
            L1VpnSpinnerRing.Visibility = isConnecting ? Visibility.Visible : Visibility.Collapsed;
            L1VpnSpinnerRing.Stroke = mainBrush;

            L1VpnPowerRing.Stroke = accentBrush;
            L1VpnPowerDisc.Background = discBrush;
            L1VpnPowerIcon.Foreground = accentBrush;
            L1VpnPowerGlow.Color = glowColor;
            L1VpnPowerGlow.Opacity = isConnecting ? 0.8 : 0.55;
        }

        private void UpdateTrafficUI()
        {
            var stats = VpnService.Instance.Traffic;
            string trafficText = $"Upload {stats.UploadSpeedDisplay}  ·  Download {stats.DownloadSpeedDisplay}";
            L2TrafficText.Text = trafficText;
            L2HeroTrafficText.Text = trafficText;
        }

        public void RefreshServersList()
        {
            var vpn = VpnService.Instance;
            foreach (var server in vpn.Servers)
            {
                server.IsActive = server.Id == vpn.ActiveServer?.Id;
            }

            ServersItemsControl.ItemsSource = null;
            ServersItemsControl.ItemsSource = vpn.Servers.ToList();
        }

        private async void VpnConnectToggle_Click(object sender, RoutedEventArgs e)
        {
            // Click bounce animation
            var bounceAnim = new DoubleAnimation
            {
                From = 0.82,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(260),
                EasingFunction = new ElasticEase { Oscillations = 1, Springiness = 4, EasingMode = EasingMode.EaseOut }
            };
            L1VpnPowerScale.BeginAnimation(ScaleTransform.ScaleXProperty, bounceAnim);
            L1VpnPowerScale.BeginAnimation(ScaleTransform.ScaleYProperty, bounceAnim);

            await VpnService.Instance.ToggleConnectionAsync();
            RefreshVpnUI();
        }

        private void OpenVpnServersLevel2_Click(object sender, RoutedEventArgs e)
        {
            OpenLevel2();
        }

        private void BackToLevel1_Click(object sender, RoutedEventArgs e)
        {
            BackToLevel1();
        }

        private void ServerRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is VpnServerProfile server)
            {
                VpnService.Instance.SelectServer(server);
                RefreshVpnUI();
                RefreshServersList();
            }
        }

        private async void PingAll_Click(object sender, RoutedEventArgs e)
        {
            if (_isPingingAll) return;
            _isPingingAll = true;
            SpeedTestBtnText.Text = "Testing server latency…";

            try
            {
                var servers = VpnService.Instance.Servers.ToList();
                var tasks = servers.Select(s => SpeedTestService.TestServerAsync(s));
                await Task.WhenAll(tasks);
                RefreshServersList();
            }
            catch { }
            finally
            {
                _isPingingAll = false;
                SpeedTestBtnText.Text = "Run speed test";
            }
        }

        private async void UpdateSubs_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingSubs) return;
            _isUpdatingSubs = true;
            UpdateSubsBtnText.Text = "Updating subscriptions…";

            try
            {
                await SubscriptionManagerService.UpdateAllSubscriptionsAsync();
                RefreshServersList();
                RefreshVpnUI();
            }
            catch { }
            finally
            {
                _isUpdatingSubs = false;
                UpdateSubsBtnText.Text = "Update subscriptions";
            }
        }

        #endregion

        #region Music Player Module Handlers

        private void OnMusicTrackChanged(AudioTrackItem? track)
        {
            Dispatcher.Invoke(() => RefreshMusicUI());
        }

        private void OnMusicPlaybackStateChanged(bool isPlaying)
        {
            Dispatcher.Invoke(() => RefreshMusicUI());
        }

        private void OnMusicPositionChanged(TimeSpan position, TimeSpan duration)
        {
            Dispatcher.Invoke(() => UpdateMusicPositionUI(position, duration));
        }

        private void OnMusicVolumeChanged(double volumePercent)
        {
            Dispatcher.Invoke(() =>
            {
                if (MiniVolumePopup.IsOpen && !_syncingVolume)
                {
                    _syncingVolume = true;
                    MiniFlexVolume.Value = volumePercent;
                    _syncingVolume = false;
                }
            });
        }

        private void OnMusicFavoritesUpdated()
        {
            Dispatcher.Invoke(() =>
            {
                var current = LocalAudioPlayerService.Instance.CurrentTrack;
                UpdateLikeButtonUI(current);
            });
        }

        private void RefreshMusicUI()
        {
            var player = LocalAudioPlayerService.Instance;
            var current = player.CurrentTrack;

            if (current != null)
            {
                L1MusicTitleText.Text = current.DisplayTitle;
                L1MusicTitleText.ToolTip = current.DisplayTitle;
                L1MusicArtistText.Text = current.DisplayArtist;
                L1MusicArtistText.ToolTip = current.DisplayArtist;

                if (current.AlbumArt != null)
                {
                    L1MusicCoverImg.Source = current.AlbumArt;
                    L1MusicCoverImg.Visibility = Visibility.Visible;
                }
                else
                {
                    L1MusicCoverImg.Source = null;
                    L1MusicCoverImg.Visibility = Visibility.Collapsed;
                }

                UpdateLikeButtonUI(current);
                if (!_isDraggingSlider)
                {
                    UpdateMusicPositionUI(player.CurrentPosition, current.Duration);
                }
            }
            else
            {
                L1MusicTitleText.Text = "No Track Playing";
                L1MusicTitleText.ToolTip = null;
                L1MusicArtistText.Text = "Select a song";
                L1MusicArtistText.ToolTip = null;
                L1MusicCoverImg.Source = null;
                L1MusicCoverImg.Visibility = Visibility.Collapsed;
                UpdateLikeButtonUI(null);
                if (!_isDraggingSlider)
                {
                    UpdateMusicPositionUI(TimeSpan.Zero, TimeSpan.Zero);
                }
            }

            L1MusicPlayIcon.Visibility = player.IsPlaying ? Visibility.Collapsed : Visibility.Visible;
            L1MusicPauseGlyph.Visibility = player.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdateLikeButtonUI(AudioTrackItem? track)
        {
            if (track != null && track.IsFavorite)
            {
                L1MusicLikeBtn.Foreground = new SolidColorBrush(Color.FromRgb(239, 68, 68));
                L1MusicLikeBtn.ToolTip = "Favorited · Click to remove";
            }
            else
            {
                L1MusicLikeBtn.Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255));
                L1MusicLikeBtn.ToolTip = "Add to Favorites";
            }
        }

        private void UpdateMusicPositionUI(TimeSpan position, TimeSpan duration)
        {
            if (_isDraggingSlider) return;

            double maxSeconds = duration.TotalSeconds > 0 ? duration.TotalSeconds : 100;
            double positionSeconds = Math.Clamp(position.TotalSeconds, 0, maxSeconds);

            L1MusicProgressSlider.Maximum = maxSeconds;
            L1MusicProgressSlider.Value = positionSeconds;
            L1MusicElapsedText.Text = $"{position:mm\\:ss}";
            L1MusicDurationText.Text = duration.TotalSeconds > 0 ? $"{duration:mm\\:ss}" : "00:00";
        }

        private void MusicPlayPause_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.TogglePlayPause();
            RefreshMusicUI();
        }

        private void MusicNext_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayNext();
            RefreshMusicUI();
        }

        private void MusicPrev_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayPrevious();
            RefreshMusicUI();
        }

        private void L1MusicLikeBtn_Click(object sender, RoutedEventArgs e)
        {
            var track = LocalAudioPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                LocalAudioPlayerService.Instance.ToggleFavorite(track);
                UpdateLikeButtonUI(track);
            }
        }

        private void ExpandMusicPlayer_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ShowMusicPlayerWidget();
            }
        }

        private void L1MusicVolumeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is UIElement target)
            {
                MiniVolumePopup.PlacementTarget = target;
            }

            MiniVolumePopup.IsOpen = !MiniVolumePopup.IsOpen;
            if (MiniVolumePopup.IsOpen)
            {
                _syncingVolume = true;
                MiniFlexVolume.Value = LocalAudioPlayerService.Instance.CurrentVolume * 100.0;
                _syncingVolume = false;
            }
        }

        private void MiniFlexVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncingVolume) return;
            LocalAudioPlayerService.Instance.SetVolume(e.NewValue);
        }

        private void L1MusicProgressSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = true;
        }

        private void L1MusicProgressSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isDraggingSlider = false;
            LocalAudioPlayerService.Instance.Seek(L1MusicProgressSlider.Value);
        }

        private void L1MusicProgressSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isDraggingSlider)
            {
                L1MusicElapsedText.Text = TimeSpan.FromSeconds(e.NewValue).ToString(@"mm\:ss");
            }
        }

        #endregion

        #region Desktop Fences Module Handlers

        private void CreateNewFence_Click(object sender, RoutedEventArgs e)
        {
            DesktopFenceManager.Instance.CreateFence("Shortcuts Fence", FenceType.CustomShortcuts);
        }

        private void CreatePortalFence_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Select Folder for Live Portal Fence"
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string path = dialog.SelectedPath;
                string title = System.IO.Path.GetFileName(path);
                if (string.IsNullOrWhiteSpace(title)) title = "Folder Portal";
                DesktopFenceManager.Instance.CreateFolderPortalFence(title, path);
            }
        }

        #endregion

        #region Sticky Notes Module Handlers

        private void CreateNewNote_Click(object sender, RoutedEventArgs e)
        {
            StickyNoteManager.Instance.CreateNewNote();
        }

        private void ShowAllNotes_Click(object sender, RoutedEventArgs e)
        {
            StickyNoteManager.Instance.ShowAllNotes();
        }

        private void HideAllNotes_Click(object sender, RoutedEventArgs e)
        {
            StickyNoteManager.Instance.HideAllNotes();
        }

        #endregion

        #region Window Events & Lifetime

        private void ClosePanel_Click(object sender, RoutedEventArgs e)
        {
            CollapsePanel();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (_currentState != PanelState.Collapsed)
            {
                CollapsePanel();
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_currentState == PanelState.Level2)
                {
                    BackToLevel1();
                }
                else if (_currentState == PanelState.Level1)
                {
                    CollapsePanel();
                }
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Clean up timers & event subscriptions
            _trafficTimer.Stop();

            VpnService.Instance.StateChanged -= OnVpnStateChanged;
            VpnService.Instance.CoreManager.TrafficUpdated -= OnVpnTrafficUpdated;

            LocalAudioPlayerService.Instance.OnTrackChanged -= OnMusicTrackChanged;
            LocalAudioPlayerService.Instance.OnPlaybackStateChanged -= OnMusicPlaybackStateChanged;
            LocalAudioPlayerService.Instance.OnPositionChanged -= OnMusicPositionChanged;
            LocalAudioPlayerService.Instance.OnVolumeChanged -= OnMusicVolumeChanged;
            LocalAudioPlayerService.Instance.OnFavoritesUpdated -= OnMusicFavoritesUpdated;
        }

        #endregion
    }
}

