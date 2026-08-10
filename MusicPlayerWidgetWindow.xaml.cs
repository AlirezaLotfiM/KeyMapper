using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class MusicPlayerWidgetWindow : Window
    {
        private bool _isUserSeeking;
        private bool _isMiniMode;
        private string _activeTab = "QUEUE"; // QUEUE, PLAYLISTS, ALL, GENRES, ARTISTS, FAVS, HISTORY
        private CustomPlaylist? _selectedCustomPlaylist;
        private Storyboard? _spinStoryboard;
        private Storyboard? _miniBackdropStoryboard;
        private readonly DispatcherTimer _grooveTimer;
        private int _grooveSeed;
        private double _groovePhase;
        private bool _isRestoringPreferences = true;
        private bool _playlistVisiblePreference = true;

        public MusicPlayerWidgetWindow()
        {
            InitializeComponent();
            Topmost = false;

            _spinStoryboard = (Storyboard)FindResource("SpinDiscStoryboard");
            _miniBackdropStoryboard =
                (Storyboard)FindResource("MiniBackdropDriftStoryboard");
            MiniGrainOverlay.Fill = CreateMiniGrainBrush();
            _grooveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(85)
            };
            _grooveTimer.Tick += GrooveTimer_Tick;

            LocalAudioPlayerService.Instance.OnTrackChanged += Instance_OnTrackChanged;
            LocalAudioPlayerService.Instance.OnPlaybackStateChanged += Instance_OnPlaybackStateChanged;
            LocalAudioPlayerService.Instance.OnPositionChanged += Instance_OnPositionChanged;
            LocalAudioPlayerService.Instance.OnVolumeChanged += Instance_OnVolumeChanged;
            LocalAudioPlayerService.Instance.OnFavoritesUpdated += Instance_OnFavoritesUpdated;
            LocalAudioPlayerService.Instance.OnQueueUpdated += Instance_OnQueueUpdated;
            LocalAudioPlayerService.Instance.OnCustomPlaylistsUpdated += Instance_OnCustomPlaylistsUpdated;
            LocalAudioPlayerService.Instance.OnHistoryUpdated += Instance_OnHistoryUpdated;
            LocalAudioPlayerService.Instance.OnPlayCountsUpdated += Instance_OnPlayCountsUpdated;

            AppSettings settings = ConfigManager.Load();
            _activeTab = NormalizeMusicTab(settings.MusicPlayerActiveTab);
            _isMiniHorizontal = settings.MusicPlayerMiniHorizontal;
            _playlistVisiblePreference =
                settings.MusicPlayerPlaylistVisible;
            SortComboBox.SelectedIndex = Math.Clamp(
                settings.MusicPlayerSortIndex,
                0,
                Math.Max(0, SortComboBox.Items.Count - 1));
            if (settings.MusicPlayerMiniMode)
            {
                ToggleMiniPlayerMode();
            }
            else if (!settings.MusicPlayerPlaylistVisible)
            {
                PlaylistView.Visibility = Visibility.Collapsed;
                Height = 260;
            }
            if (settings.MusicPlayerVolume >= 0 && settings.MusicPlayerVolume <= 100)
            {
                LocalAudioPlayerService.Instance.SetVolume(settings.MusicPlayerVolume);
            }
            double currentVol = LocalAudioPlayerService.Instance.CurrentVolume * 100.0;
            if (VolumeSlider != null) VolumeSlider.Value = currentVol;
            if (MiniVolumeSlider != null) MiniVolumeSlider.Value = currentVol;
            if (MiniVolumeSliderH != null) MiniVolumeSliderH.Value = currentVol;
            if (MiniVolumePopupSlider != null) MiniVolumePopupSlider.Value = currentVol;
            UpdateMiniPopupVolume(currentVol);

            UpdateTabStyles();
            _isRestoringPreferences = false;

            _ = InitializeLibraryAsync();
        }

        private void GrooveTimer_Tick(object? sender, EventArgs e)
        {
            var bars = new[]
            {
                GrooveBar1, GrooveBar2, GrooveBar3, GrooveBar4,
                GrooveBar5, GrooveBar6, GrooveBar7
            };
            double trackPace = 0.72 + Math.Abs(_grooveSeed % 37) / 48.0;
            _groovePhase += trackPace;

            for (int i = 0; i < bars.Length; i++)
            {
                double phase = _groovePhase + i * (0.62 + Math.Abs(_grooveSeed % 11) / 30.0);
                double primary = Math.Abs(Math.Sin(phase));
                double secondary = Math.Abs(Math.Sin(phase * 0.43 + (_grooveSeed & 15)));
                bars[i].Height = 4 + (primary * 9) + (secondary * 5);
            }
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            hwndSource?.AddHook(WndProc);
        }

        private const int WM_APPCOMMAND = 0x0319;
        private const int APPCOMMAND_MEDIA_NEXTTRACK = 11;
        private const int APPCOMMAND_MEDIA_PREVTRACK = 12;
        private const int APPCOMMAND_MEDIA_STOP = 13;
        private const int APPCOMMAND_MEDIA_PLAY_PAUSE = 14;
        private const int APPCOMMAND_MEDIA_PAUSE = 47;
        private const int APPCOMMAND_MEDIA_PLAY = 46;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_APPCOMMAND)
            {
                int cmd = (int)((long)lParam >> 16) & ~0xF000;
                switch (cmd)
                {
                    case APPCOMMAND_MEDIA_PLAY_PAUSE:
                        LocalAudioPlayerService.Instance.TogglePlayPause();
                        handled = true;
                        break;
                    case APPCOMMAND_MEDIA_PLAY:
                        if (!LocalAudioPlayerService.Instance.IsPlaying) LocalAudioPlayerService.Instance.TogglePlayPause();
                        handled = true;
                        break;
                    case APPCOMMAND_MEDIA_PAUSE:
                    case APPCOMMAND_MEDIA_STOP:
                        if (LocalAudioPlayerService.Instance.IsPlaying) LocalAudioPlayerService.Instance.TogglePlayPause();
                        handled = true;
                        break;
                    case APPCOMMAND_MEDIA_NEXTTRACK:
                        LocalAudioPlayerService.Instance.PlayNext();
                        handled = true;
                        break;
                    case APPCOMMAND_MEDIA_PREVTRACK:
                        LocalAudioPlayerService.Instance.PlayPrevious();
                        handled = true;
                        break;
                }
            }
            return IntPtr.Zero;
        }

        private async System.Threading.Tasks.Task InitializeLibraryAsync()
        {
            if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
            try
            {
                await LocalAudioPlayerService.Instance.ScanLibraryAsync();
                UpdateRepeatButtonUI();
                UpdateShuffleButtonUI();
                UpdatePlaylistList();
            }
            finally
            {
                if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private bool _isPinned = true;

        private void TogglePin_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = !_isPinned;
            UpdatePinStateUI();
        }

        private void UpdatePinStateUI()
        {
            Topmost = _isMiniMode && _isPinned;

            if (MiniPinIconV != null) MiniPinIconV.Visibility = _isPinned ? Visibility.Collapsed : Visibility.Visible;
            if (MiniUnpinIconV != null) MiniUnpinIconV.Visibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;
            if (MiniPinIconH != null) MiniPinIconH.Visibility = _isPinned ? Visibility.Collapsed : Visibility.Visible;
            if (MiniUnpinIconH != null) MiniUnpinIconH.Visibility = _isPinned ? Visibility.Visible : Visibility.Collapsed;

            if (MiniPinBtnV != null) MiniPinBtnV.ToolTip = _isPinned ? "Unpin Mini Player (Un-float)" : "Pin Mini Player (Stay on top)";
            if (MiniPinBtnH != null) MiniPinBtnH.ToolTip = _isPinned ? "Unpin Mini Player (Un-float)" : "Pin Mini Player (Stay on top)";
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void TaskbarPrev_Click(object sender, EventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayPrevious();
        }

        private void TaskbarPlayPause_Click(object sender, EventArgs e)
        {
            LocalAudioPlayerService.Instance.TogglePlayPause();
        }

        private void TaskbarNext_Click(object sender, EventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayNext();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            ToggleMiniPlayerMode();
        }

        private void ExpandMiniPlayer_Click(object sender, RoutedEventArgs e)
        {
            ToggleMiniPlayerMode();
        }

        private void TogglePlaylistBtn_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistView.Visibility == Visibility.Visible)
            {
                PlaylistView.Visibility = Visibility.Collapsed;
                Height = 260;
                _playlistVisiblePreference = false;
            }
            else
            {
                PlaylistView.Visibility = Visibility.Visible;
                Height = 650;
                _playlistVisiblePreference = true;
            }
            SaveMusicPlayerPreferences();
        }

        private bool _isMiniHorizontal = false;

        private void ToggleMiniOrientation_Click(object sender, RoutedEventArgs e)
        {
            _isMiniHorizontal = !_isMiniHorizontal;
            ApplyMiniOrientation();
            SaveMusicPlayerPreferences();
        }

        private void ApplyMiniOrientation()
        {
            if (!_isMiniMode) return;
            Rect workArea = SystemParameters.WorkArea;
            if (_isMiniHorizontal)
            {
                MiniVerticalLayout.Visibility = Visibility.Collapsed;
                MiniHorizontalLayout.Visibility = Visibility.Visible;
                if (MiniBackdropLayoutScale != null)
                {
                    MiniBackdropLayoutScale.ScaleX = 1.00;
                    MiniBackdropLayoutScale.ScaleY = 1.00;
                }
                if (MiniBackdropImage != null)
                {
                    MiniBackdropImage.Opacity = 0.82;
                }
                if (MiniBackdropDarkOverlay != null)
                {
                    MiniBackdropDarkOverlay.Opacity = 0.52;
                }
                if (MiniLandscapeVignette != null)
                {
                    MiniLandscapeVignette.Visibility = Visibility.Collapsed;
                }
                if (MiniArtworkGradient != null)
                {
                    MiniArtworkGradient.Opacity = 0.42;
                }
                if (MiniGrainOverlay != null)
                {
                    MiniGrainOverlay.Opacity = 0.30;
                }
                if (MiniArtworkBackdrop != null)
                {
                    MiniArtworkBackdrop.Background = null;
                    MiniArtworkBackdrop.BorderBrush = null;
                    MiniArtworkBackdrop.BorderThickness = new Thickness(0);
                }
                Width = 455;
                Height = 150;
                Left = workArea.Right - Width - 20;
                Top = workArea.Bottom - Height - 20;
            }
            else
            {
                MiniHorizontalLayout.Visibility = Visibility.Collapsed;
                MiniVerticalLayout.Visibility = Visibility.Visible;
                if (MiniBackdropLayoutScale != null)
                {
                    MiniBackdropLayoutScale.ScaleX = 1.28;
                    MiniBackdropLayoutScale.ScaleY = 1.28;
                }
                if (MiniBackdropImage != null)
                {
                    MiniBackdropImage.Opacity = 0.82;
                }
                if (MiniBackdropDarkOverlay != null)
                {
                    MiniBackdropDarkOverlay.Opacity = 0.52;
                }
                if (MiniLandscapeVignette != null)
                {
                    MiniLandscapeVignette.Visibility = Visibility.Collapsed;
                }
                if (MiniArtworkGradient != null)
                {
                    MiniArtworkGradient.Opacity = 0.42;
                }
                if (MiniGrainOverlay != null)
                {
                    MiniGrainOverlay.Opacity = 0.30;
                }
                if (MiniArtworkBackdrop != null)
                {
                    MiniArtworkBackdrop.Background = null;
                    MiniArtworkBackdrop.BorderBrush = null;
                    MiniArtworkBackdrop.BorderThickness = new Thickness(0);
                }
                Width = 180;
                Height = 270;
                Left = workArea.Right - Width - 20;
                Top = workArea.Top + 20;
            }
            UpdateOuterWindowClip();
        }

        private void ToggleMiniPlayerMode()
        {
            _isMiniMode = !_isMiniMode;
            Rect workArea = SystemParameters.WorkArea;

            if (_isMiniMode)
            {
                HeaderGrid.Visibility = Visibility.Collapsed;
                PlayerMainView.Visibility = Visibility.Collapsed;
                PlaylistView.Visibility = Visibility.Collapsed;
                MiniDeckView.Visibility = Visibility.Visible;

                OuterWindowBorder.CornerRadius = new CornerRadius(16);
                OuterWindowBorder.Margin = new Thickness(2);
                OuterWindowBorder.BorderThickness = new Thickness(0);
                OuterWindowBorder.Background = Brushes.Transparent;
                MainRootGrid.Margin = new Thickness(0);

                _miniBackdropStoryboard?.Begin(this, true);
                ApplyMiniOrientation();
                UpdatePinStateUI();
            }
            else
            {
                MiniDeckView.Visibility = Visibility.Collapsed;
                HeaderGrid.Visibility = Visibility.Visible;
                PlayerMainView.Visibility = Visibility.Visible;
                PlaylistView.Visibility = _playlistVisiblePreference
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                OuterWindowBorder.CornerRadius = new CornerRadius(24);
                OuterWindowBorder.Margin = new Thickness(10);
                OuterWindowBorder.BorderThickness = new Thickness(1.8);
                OuterWindowBorder.SetResourceReference(
                    Border.BackgroundProperty,
                    "AppBackgroundBrush");
                MainRootGrid.Margin = new Thickness(16);

                Width = 500;
                Height = _playlistVisiblePreference ? 650 : 260;
                Topmost = false;
                _miniBackdropStoryboard?.Remove(this);

                Left = Math.Max(0, (workArea.Width - Width) / 2 + workArea.Left);
                Top = Math.Max(0, (workArea.Height - Height) / 2 + workArea.Top);
            }
            UpdateOuterWindowClip();
            SaveMusicPlayerPreferences();
        }

        private void OuterWindowBorder_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            UpdateOuterWindowClip();
        }

        private void UpdateOuterWindowClip()
        {
            if (OuterWindowBorder == null ||
                OuterWindowBorder.ActualWidth <= 0 ||
                OuterWindowBorder.ActualHeight <= 0)
            {
                return;
            }

            double radius = OuterWindowBorder.CornerRadius.TopLeft;
            OuterWindowBorder.Clip = new RectangleGeometry(
                new Rect(
                    0,
                    0,
                    OuterWindowBorder.ActualWidth,
                    OuterWindowBorder.ActualHeight),
                radius,
                radius);
        }

        private async void ManageFoldersBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new ManageMusicFoldersWindow { Owner = this };
            win.ShowDialog();

            if (!win.LibraryChanged)
            {
                return;
            }

            if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Visible;
            try
            {
                await LocalAudioPlayerService.Instance.ScanLibraryAsync();
                UpdatePlaylistList();
            }
            finally
            {
                if (LoadingOverlay != null) LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void PlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.TogglePlayPause();
        }

        private void NextBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayNext();
        }

        private void PrevBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayPrevious();
        }

        private void ShuffleBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.ToggleShuffle();
            UpdateShuffleButtonUI();
            UpdatePlaylistList();
        }

        private void UpdateShuffleButtonUI()
        {
            ShuffleBtn.Foreground = (Brush)FindResource(
                LocalAudioPlayerService.Instance.IsShuffle
                    ? "AppAccentFillBrush"
                    : "AppMutedTextBrush");
            ShuffleBtn.Opacity =
                LocalAudioPlayerService.Instance.IsShuffle ? 1 : 0.6;
        }

        private void RepeatBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.CycleRepeatMode();
            UpdateRepeatButtonUI();
        }

        private void UpdateRepeatButtonUI()
        {
            var mode = LocalAudioPlayerService.Instance.RepeatMode;
            RepeatOffIcon.Visibility =
                mode == RepeatMode.Off ? Visibility.Visible : Visibility.Collapsed;
            RepeatAllIcon.Visibility =
                mode == RepeatMode.RepeatAll ? Visibility.Visible : Visibility.Collapsed;
            RepeatOneIcon.Visibility =
                mode == RepeatMode.RepeatOne ? Visibility.Visible : Visibility.Collapsed;

            switch (mode)
            {
                case RepeatMode.Off:
                    RepeatBtn.ToolTip = "Repeat: Off";
                    break;
                case RepeatMode.RepeatAll:
                    RepeatBtn.ToolTip = "Repeat: All Tracks";
                    break;
                case RepeatMode.RepeatOne:
                    RepeatBtn.ToolTip = "Repeat: One Track";
                    break;
            }
        }

        private void LikeBtn_Click(object sender, RoutedEventArgs e)
        {
            var track = LocalAudioPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                LocalAudioPlayerService.Instance.ToggleFavorite(track);
                UpdateLikeButtonUI(track);
            }
        }

        private void ItemFavorite_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is AudioTrackItem track)
            {
                LocalAudioPlayerService.Instance.ToggleFavorite(track);
                UpdatePlaylistList();
            }
        }

        private void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LocalAudioPlayerService.Instance.SetVolume(e.NewValue);
        }

        private void MiniVolumeButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is UIElement target)
            {
                MiniVolumePopup.PlacementTarget = target;
            }

            MiniVolumePopup.IsOpen = !MiniVolumePopup.IsOpen;
            if (MiniVolumePopup.IsOpen)
            {
                double current = LocalAudioPlayerService.Instance.CurrentVolume * 100.0;
                if (Math.Abs(MiniVolumePopupSlider.Value - current) > 0.5)
                {
                    MiniVolumePopupSlider.Value = current;
                }
                UpdateMiniPopupVolume(current);
            }
        }

        private void VolumePopupSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LocalAudioPlayerService.Instance.SetVolume(e.NewValue);
            UpdateMiniPopupVolume(e.NewValue);
        }

        private void UpdateMiniPopupVolume(double volumePercent)
        {
            double clamped = Math.Clamp(volumePercent, 0, 100);
            if (MiniPopupVolumeFill != null)
            {
                MiniPopupVolumeFill.Height = 154 * clamped / 100.0;
            }
            if (MiniPopupVolumeText != null)
            {
                MiniPopupVolumeText.Text = $"{Math.Round(clamped):0}%";
            }
        }

        private void Instance_OnVolumeChanged(double volumePercent)
        {
            Dispatcher.Invoke(() =>
            {
                if (VolumeSlider != null && Math.Abs(VolumeSlider.Value - volumePercent) > 0.5)
                {
                    VolumeSlider.Value = volumePercent;
                }
                if (MiniVolumeSlider != null && Math.Abs(MiniVolumeSlider.Value - volumePercent) > 0.5)
                {
                    MiniVolumeSlider.Value = volumePercent;
                }
                if (MiniVolumeSliderH != null && Math.Abs(MiniVolumeSliderH.Value - volumePercent) > 0.5)
                {
                    MiniVolumeSliderH.Value = volumePercent;
                }
                if (MiniVolumePopupSlider != null && Math.Abs(MiniVolumePopupSlider.Value - volumePercent) > 0.5)
                {
                    MiniVolumePopupSlider.Value = volumePercent;
                }
                UpdateMiniPopupVolume(volumePercent);
            });
        }

        private void Instance_OnTrackChanged(AudioTrackItem? track)
        {
            Dispatcher.Invoke(() =>
            {
                if (track != null)
                {
                    _grooveSeed = StringComparer.OrdinalIgnoreCase.GetHashCode(track.FilePath);
                    _groovePhase = 0;
                    TrackTitleTxt.Text = track.DisplayTitle;
                    MiniTrackTitleTxt.Text = track.DisplayTitle;
                    if (MiniTrackTitleTxtH != null) MiniTrackTitleTxtH.Text = track.DisplayTitle;

                    SetClickableArtistText(TrackArtistTxt, track.DisplayArtist, false);
                    SetClickableArtistText(MiniTrackArtistTxt, track.DisplayArtist, true);
                    if (MiniTrackArtistTxtH != null) SetClickableArtistText(MiniTrackArtistTxtH, track.DisplayArtist, true);

                    TotalTimeTxt.Text = track.DurationText;
                    MiniTotalTimeTxt.Text = track.DurationText;
                    if (MiniTotalTimeTxtH != null) MiniTotalTimeTxtH.Text = track.DurationText;
                    UpdateLikeButtonUI(track);

                    if (track.AlbumArt == null)
                    {
                        track.AlbumArt = LocalAudioPlayerService.Instance.ExtractEmbeddedCoverArt(track.FilePath);
                    }

                    if (track.AlbumArt != null)
                    {
                        AlbumCoverImage.Source = track.AlbumArt;
                        AlbumCoverImage.Visibility = Visibility.Visible;
                        MiniCoverImage.Source = track.AlbumArt;
                        MiniCoverImage.Visibility = Visibility.Visible;
                        if (MiniCoverImageH != null)
                        {
                            MiniCoverImageH.Source = track.AlbumArt;
                            MiniCoverImageH.Visibility = Visibility.Visible;
                        }
                        UpdateMiniArtworkBackdrop(track.AlbumArt);
                    }
                    else
                    {
                        AlbumCoverImage.Visibility = Visibility.Collapsed;
                        MiniCoverImage.Visibility = Visibility.Collapsed;
                        if (MiniCoverImageH != null) MiniCoverImageH.Visibility = Visibility.Collapsed;
                        UpdateMiniArtworkBackdrop(null);
                    }

                    UpdatePlaylistList();
                }
            });
        }

        private void SetClickableArtistText(TextBlock textBlock, string rawArtists, bool isMini)
        {
            if (textBlock == null) return;
            textBlock.Inlines.Clear();
            if (string.IsNullOrWhiteSpace(rawArtists))
            {
                textBlock.Inlines.Add(new System.Windows.Documents.Run("Unknown Artist"));
                return;
            }

            string[] delims = new[] { ";", ",", "&", "/", "\\", " ft. ", " FEAT. ", " feat. ", " WITH ", " with ", " AND ", " and " };
            var artistNames = rawArtists.Split(delims, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim())
                .Where(a => !string.IsNullOrWhiteSpace(a))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (artistNames.Count == 0)
            {
                textBlock.Inlines.Add(new System.Windows.Documents.Run(rawArtists));
                return;
            }

            for (int i = 0; i < artistNames.Count; i++)
            {
                string artistName = artistNames[i];
                var link = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(artistName))
                {
                    Foreground = isMini ? (Brush)FindResource("MiniForegroundBrush") : (Brush)FindResource("AppAccentBrush"),
                    TextDecorations = null,
                    Cursor = Cursors.Hand
                };
                string targetArtist = artistName;
                link.Click += (s, e) => FilterByArtistName(targetArtist);
                textBlock.Inlines.Add(link);

                if (i < artistNames.Count - 1)
                {
                    textBlock.Inlines.Add(new System.Windows.Documents.Run(", ")
                    {
                        Foreground = isMini ? (Brush)FindResource("MiniMutedBrush") : (Brush)FindResource("AppMutedTextBrush")
                    });
                }
            }
        }

        private string? _artistFilter = null;

        private void FilterByArtistName(string artistName)
        {
            if (_isMiniMode)
            {
                ToggleMiniPlayerMode();
            }

            _artistFilter = artistName;
            _selectedCustomPlaylist = null;
            _activeTab = "ALL";
            UpdateTabStyles();
            UpdatePlaylistList();
        }

        private void ClearArtistFilter_Click(object sender, RoutedEventArgs e)
        {
            _artistFilter = null;
            UpdatePlaylistList();
        }

        private static bool IsTrackByArtist(AudioTrackItem track, string targetArtist)
        {
            if (string.IsNullOrWhiteSpace(targetArtist)) return true;
            string artistStr = track.DisplayArtist;
            if (string.IsNullOrWhiteSpace(artistStr)) return false;

            string[] delims = new[] { ";", ",", "&", "/", "\\", " ft. ", " FEAT. ", " feat. ", " WITH ", " with ", " AND ", " and " };
            var artists = artistStr.Split(delims, StringSplitOptions.RemoveEmptyEntries)
                .Select(a => a.Trim());

            return artists.Any(a => string.Equals(a, targetArtist, StringComparison.OrdinalIgnoreCase));
        }

        private void UpdateMiniArtworkBackdrop(BitmapSource? artwork)
        {
            if (artwork == null)
            {
                MiniBackdropImage.Source = null;
                MiniBackdropImage.Visibility = Visibility.Collapsed;
                MiniArtworkGradient.Fill =
                    (Brush)FindResource("AppAccentSoftBrush");
                MiniArtworkGradient.Opacity =
                    _isMiniHorizontal ? 0.24 : 0.38;
                return;
            }

            MiniBackdropImage.Source = artwork;
            MiniBackdropImage.Visibility = Visibility.Visible;

            try
            {
                double scale = Math.Min(
                    24.0 / Math.Max(1, artwork.PixelWidth),
                    24.0 / Math.Max(1, artwork.PixelHeight));
                var sampled = new TransformedBitmap(
                    artwork,
                    new ScaleTransform(scale, scale));
                var converted = new FormatConvertedBitmap(
                    sampled,
                    PixelFormats.Bgra32,
                    null,
                    0);

                int stride = converted.PixelWidth * 4;
                byte[] pixels = new byte[stride * converted.PixelHeight];
                converted.CopyPixels(pixels, stride, 0);

                Color upper = AverageArtworkColor(
                    pixels,
                    converted.PixelWidth,
                    converted.PixelHeight,
                    stride,
                    0,
                    Math.Max(1, converted.PixelHeight / 2));
                Color lower = AverageArtworkColor(
                    pixels,
                    converted.PixelWidth,
                    converted.PixelHeight,
                    stride,
                    converted.PixelHeight / 2,
                    converted.PixelHeight);

                var gradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(1, 1)
                };
                gradient.GradientStops.Add(
                    new GradientStop(
                        Color.FromArgb(
                            205,
                            upper.R,
                            upper.G,
                            upper.B),
                        0));
                gradient.GradientStops.Add(
                    new GradientStop(
                        Color.FromArgb(
                            180,
                            lower.R,
                            lower.G,
                            lower.B),
                        1));
                gradient.Freeze();

                MiniArtworkGradient.Fill = gradient;
                MiniArtworkGradient.Opacity =
                    _isMiniHorizontal ? 0.24 : 0.42;
            }
            catch
            {
                MiniArtworkGradient.Fill =
                    (Brush)FindResource("AppAccentSoftBrush");
                MiniArtworkGradient.Opacity =
                    _isMiniHorizontal ? 0.24 : 0.38;
            }
        }

        private static ImageBrush CreateMiniGrainBrush()
        {
            const int size = 48;
            const int bytesPerPixel = 4;
            int stride = size * bytesPerPixel;
            byte[] pixels = new byte[stride * size];
            var random = new Random(271828);

            for (int index = 0; index < pixels.Length;
                 index += bytesPerPixel)
            {
                bool lightGrain = random.NextDouble() > 0.48;
                byte shade = lightGrain ? (byte)255 : (byte)0;
                byte alpha = (byte)random.Next(6, 24);
                pixels[index] = shade;
                pixels[index + 1] = shade;
                pixels[index + 2] = shade;
                pixels[index + 3] = alpha;
            }

            BitmapSource grain = BitmapSource.Create(
                size,
                size,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride);
            grain.Freeze();

            var brush = new ImageBrush(grain)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, size, size),
                ViewportUnits = BrushMappingMode.Absolute,
                Stretch = Stretch.None
            };
            brush.Freeze();
            return brush;
        }

        private static Color AverageArtworkColor(
            byte[] pixels,
            int width,
            int height,
            int stride,
            int startRow,
            int endRow)
        {
            long red = 0;
            long green = 0;
            long blue = 0;
            long count = 0;

            for (int y = Math.Clamp(startRow, 0, height);
                 y < Math.Clamp(endRow, 0, height);
                 y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    int index = row + (x * 4);
                    byte alpha = pixels[index + 3];
                    if (alpha < 24)
                    {
                        continue;
                    }

                    blue += pixels[index];
                    green += pixels[index + 1];
                    red += pixels[index + 2];
                    count++;
                }
            }

            if (count == 0)
            {
                return Color.FromRgb(90, 120, 145);
            }

            return Color.FromRgb(
                (byte)(red / count),
                (byte)(green / count),
                (byte)(blue / count));
        }

        private void UpdateLikeButtonUI(AudioTrackItem track)
        {
            Brush brush = (Brush)FindResource(
                track.IsFavorite
                    ? "AppAccentFillBrush"
                    : "AppMutedTextBrush");
            LikeBtn.Foreground = brush;
            LikeBtn.Opacity = track.IsFavorite ? 1 : 0.5;
            LikeBtn.ToolTip =
                track.IsFavorite ? "Remove from favorites" : "Add to favorites";
            if (MiniLikeBtn != null)
            {
                MiniLikeBtn.Foreground = track.IsFavorite
                    ? brush
                    : (Brush)FindResource("MiniMutedBrush");
                MiniLikeBtn.Opacity = track.IsFavorite ? 1 : 0.5;
                MiniLikeBtn.ToolTip = LikeBtn.ToolTip;
            }
            if (MiniLikeBtnH != null)
            {
                MiniLikeBtnH.Foreground = track.IsFavorite
                    ? brush
                    : (Brush)FindResource("MiniMutedBrush");
                MiniLikeBtnH.Opacity = track.IsFavorite ? 1 : 0.5;
                MiniLikeBtnH.ToolTip = LikeBtn.ToolTip;
            }
        }

        private void Instance_OnPlaybackStateChanged(bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                PlayPauseIcon.Visibility = isPlaying ? Visibility.Collapsed : Visibility.Visible;
                PauseGlyph.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;
                MiniPlayIcon.Visibility =
                    isPlaying ? Visibility.Collapsed : Visibility.Visible;
                MiniPauseGlyph.Visibility =
                    isPlaying ? Visibility.Visible : Visibility.Collapsed;
                MiniPlayIconH.Visibility =
                    isPlaying ? Visibility.Collapsed : Visibility.Visible;
                MiniPauseGlyphH.Visibility =
                    isPlaying ? Visibility.Visible : Visibility.Collapsed;
                EqualizerPanel.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;

                if (TaskbarPlayPauseBtn != null)
                {
                    TaskbarPlayPauseBtn.ImageSource = isPlaying
                        ? (ImageSource)FindResource("TaskbarPauseIcon")
                        : (ImageSource)FindResource("TaskbarPlayIcon");
                    TaskbarPlayPauseBtn.Description = isPlaying ? "Pause" : "Play";
                }

                if (isPlaying)
                {
                    _spinStoryboard?.Begin();
                    _grooveTimer.Start();
                }
                else
                {
                    _spinStoryboard?.Pause();
                    _grooveTimer.Stop();
                }
            });
        }

        private void Instance_OnPositionChanged(TimeSpan pos, TimeSpan total)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isUserSeeking)
                {
                    string curText = $"{pos:mm\\:ss}";
                    string totText = $"{total:mm\\:ss}";
                    CurrentTimeTxt.Text = curText;
                    TotalTimeTxt.Text = totText;
                    MiniCurrentTimeTxt.Text = curText;
                    MiniTotalTimeTxt.Text = totText;
                    if (MiniCurrentTimeTxtH != null) MiniCurrentTimeTxtH.Text = curText;
                    if (MiniTotalTimeTxtH != null) MiniTotalTimeTxtH.Text = totText;

                    double maxSec = total.TotalSeconds > 0 ? total.TotalSeconds : 100;
                    PositionSlider.Maximum = maxSec;
                    MiniPositionSlider.Maximum = maxSec;
                    if (MiniPositionSliderH != null) MiniPositionSliderH.Maximum = maxSec;

                    PositionSlider.Value = pos.TotalSeconds;
                    MiniPositionSlider.Value = pos.TotalSeconds;
                    if (MiniPositionSliderH != null) MiniPositionSliderH.Value = pos.TotalSeconds;
                }
            });
        }

        private void PositionSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = true;
        }

        private void PositionSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
        {
            _isUserSeeking = false;
            if (sender is Slider slider)
            {
                LocalAudioPlayerService.Instance.Seek(slider.Value);
            }
        }

        private void PositionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (e.NewValue >= 0 && _isUserSeeking)
            {
                LocalAudioPlayerService.Instance.Seek(e.NewValue);
            }
        }

        private void PlaylistListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (Mouse.RightButton == MouseButtonState.Pressed) return;

            if (PlaylistListBox.SelectedItem is AudioTrackItem item && PlaylistListBox.ItemsSource is IEnumerable<AudioTrackItem> activeList)
            {
                LocalAudioPlayerService.Instance.PlayTrackItem(item, activeList);
            }
        }

        private void CustomPlaylistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomPlaylistsListBox.SelectedItem is CustomPlaylist playlist)
            {
                _selectedCustomPlaylist = playlist;
                var tracks = LocalAudioPlayerService.Instance.GetCustomPlaylistTracks(playlist);
                PlaylistListBox.ItemsSource = tracks;
                PlaylistListBox.Visibility = Visibility.Visible;
                CustomPlaylistsListBox.Visibility = Visibility.Collapsed;
            }
        }

        private void ArtistsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ArtistsListBox.SelectedItem is ArtistGroupItem group)
            {
                SearchBox.Text = group.ArtistName;
                TabAll_Click(sender, e);
            }
        }

        private void GenresListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GenresListBox.SelectedItem is GenreGroupItem genreGroup)
            {
                PlaylistListBox.ItemsSource = genreGroup.Tracks;
                PlaylistListBox.Visibility = Visibility.Visible;
                GenresListBox.Visibility = Visibility.Collapsed;
            }
        }

        private void TabScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scrollViewer)
            {
                scrollViewer.ScrollToHorizontalOffset(scrollViewer.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        }

        private void ScrollTabLeft_Click(object sender, RoutedEventArgs e)
        {
            TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset - 120);
        }

        private void ScrollTabRight_Click(object sender, RoutedEventArgs e)
        {
            TabScrollViewer.ScrollToHorizontalOffset(TabScrollViewer.HorizontalOffset + 120);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaylistList();
        }

        private void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        // Navigation Tabs
        private void TabQueue_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "QUEUE";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabTopPlayed_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "TOP_PLAYED";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabPlaylists_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "PLAYLISTS";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabAll_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "ALL";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabGenres_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "GENRES";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabArtists_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "ARTISTS";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabFavs_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "FAVS";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void TabHistory_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "HISTORY";
            _artistFilter = null;
            _selectedCustomPlaylist = null;
            UpdateTabStyles();
            UpdatePlaylistList();
            SaveMusicPlayerPreferences();
        }

        private void SaveMusicPlayerPreferences()
        {
            if (_isRestoringPreferences) return;
            AppSettings settings = ConfigManager.Load();
            settings.MusicPlayerMiniMode = _isMiniMode;
            settings.MusicPlayerMiniHorizontal = _isMiniHorizontal;
            settings.MusicPlayerPlaylistVisible =
                _playlistVisiblePreference;
            settings.MusicPlayerActiveTab = _activeTab;
            settings.MusicPlayerSortIndex =
                Math.Max(0, SortComboBox?.SelectedIndex ?? 0);
            settings.MusicPlayerVolume = LocalAudioPlayerService.Instance.CurrentVolume * 100.0;
            ConfigManager.Save(settings);
        }

        private static string NormalizeMusicTab(string? tab) =>
            tab?.Trim().ToUpperInvariant() switch
            {
                "TOP_PLAYED" => "TOP_PLAYED",
                "PLAYLISTS" => "PLAYLISTS",
                "ALL" => "ALL",
                "GENRES" => "GENRES",
                "ARTISTS" => "ARTISTS",
                "FAVS" => "FAVS",
                "HISTORY" => "HISTORY",
                _ => "QUEUE"
            };

        private void Instance_OnFavoritesUpdated() => Dispatcher.Invoke(() => UpdatePlaylistList());
        private void Instance_OnQueueUpdated() => Dispatcher.Invoke(() => UpdatePlaylistList());
        private void Instance_OnCustomPlaylistsUpdated() => Dispatcher.Invoke(() => UpdatePlaylistList());
        private void Instance_OnHistoryUpdated() => Dispatcher.Invoke(() => UpdatePlaylistList());
        private void Instance_OnPlayCountsUpdated() => Dispatcher.Invoke(() => UpdatePlaylistList());

        private void UpdateTabStyles()
        {
            if (TabQueueBtn == null || PlaylistListBox == null) return;

            var cyan = (Brush)FindResource("AppAccentBrush");
            var dark = (Brush)FindResource("AppSurfaceAltBrush");
            var darkText = (Brush)FindResource("AppBackgroundBrush");
            var whiteText = (Brush)FindResource("AppTextBrush");

            TabQueueBtn.Background = _activeTab == "QUEUE" ? cyan : dark;
            TabQueueBtn.Foreground = _activeTab == "QUEUE" ? darkText : whiteText;

            TabTopPlayedBtn.Background = _activeTab == "TOP_PLAYED" ? cyan : dark;
            TabTopPlayedBtn.Foreground = _activeTab == "TOP_PLAYED" ? darkText : whiteText;

            TabPlaylistsBtn.Background = _activeTab == "PLAYLISTS" ? cyan : dark;
            TabPlaylistsBtn.Foreground = _activeTab == "PLAYLISTS" ? darkText : whiteText;

            TabAllBtn.Background = _activeTab == "ALL" ? cyan : dark;
            TabAllBtn.Foreground = _activeTab == "ALL" ? darkText : whiteText;

            TabGenresBtn.Background = _activeTab == "GENRES" ? cyan : dark;
            TabGenresBtn.Foreground = _activeTab == "GENRES" ? darkText : whiteText;

            TabArtistsBtn.Background = _activeTab == "ARTISTS" ? cyan : dark;
            TabArtistsBtn.Foreground = _activeTab == "ARTISTS" ? darkText : whiteText;

            TabFavsBtn.Background = _activeTab == "FAVS" ? cyan : dark;
            TabFavsBtn.Foreground = _activeTab == "FAVS" ? darkText : whiteText;

            TabHistoryBtn.Background = _activeTab == "HISTORY" ? cyan : dark;
            TabHistoryBtn.Foreground = _activeTab == "HISTORY" ? darkText : whiteText;

            // Visibility Toggles
            PlaylistListBox.Visibility = (_activeTab == "ARTISTS" || _activeTab == "GENRES" || (_activeTab == "PLAYLISTS" && _selectedCustomPlaylist == null)) ? Visibility.Collapsed : Visibility.Visible;
            ArtistsListBox.Visibility = _activeTab == "ARTISTS" ? Visibility.Visible : Visibility.Collapsed;
            GenresListBox.Visibility = _activeTab == "GENRES" ? Visibility.Visible : Visibility.Collapsed;
            CustomPlaylistsListBox.Visibility = (_activeTab == "PLAYLISTS" && _selectedCustomPlaylist == null) ? Visibility.Visible : Visibility.Collapsed;

            NewPlaylistBtn.Visibility = _activeTab == "PLAYLISTS" ? Visibility.Visible : Visibility.Collapsed;
            ClearQueueBtn.Visibility = (_activeTab == "QUEUE" && LocalAudioPlayerService.Instance.UserQueue.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePlaylistList()
        {
            if (PlaylistListBox == null || SearchBox == null) return;

            string query = SearchBox.Text?.Trim() ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(_artistFilter) && _activeTab == "ALL")
            {
                if (ArtistFilterBanner != null)
                {
                    ArtistFilterBanner.Visibility = Visibility.Visible;
                    FilteredArtistNameTxt.Text = $"\"{_artistFilter}\"";
                }
            }
            else
            {
                if (ArtistFilterBanner != null) ArtistFilterBanner.Visibility = Visibility.Collapsed;
            }

            if (_activeTab == "ARTISTS")
            {
                var artists = LocalAudioPlayerService.Instance.ArtistGroups;
                ArtistsListBox.ItemsSource = string.IsNullOrWhiteSpace(query) ? artists : artists.Where(a => a.ArtistName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (_activeTab == "GENRES")
            {
                var genres = LocalAudioPlayerService.Instance.GenreGroups;
                GenresListBox.ItemsSource = string.IsNullOrWhiteSpace(query) ? genres : genres.Where(g => g.GenreName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else if (_activeTab == "PLAYLISTS" && _selectedCustomPlaylist == null)
            {
                var playlists = LocalAudioPlayerService.Instance.CustomPlaylists;
                CustomPlaylistsListBox.ItemsSource = string.IsNullOrWhiteSpace(query) ? playlists : playlists.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            else
            {
                IEnumerable<AudioTrackItem> list;

                if (_activeTab == "QUEUE")
                {
                    list = LocalAudioPlayerService.Instance.ActivePlaybackQueue.AsEnumerable();
                }
                else if (_activeTab == "TOP_PLAYED")
                {
                    list = LocalAudioPlayerService.Instance.Playlist.OrderByDescending(x => x.PlayCount).ThenByDescending(x => x.LastPlayedAt);
                }
                else if (_activeTab == "PLAYLISTS" && _selectedCustomPlaylist != null)
                {
                    list = LocalAudioPlayerService.Instance.GetCustomPlaylistTracks(_selectedCustomPlaylist);
                }
                else if (_activeTab == "FAVS")
                {
                    list = LocalAudioPlayerService.Instance.Playlist.Where(x => x.IsFavorite);
                }
                else if (_activeTab == "HISTORY")
                {
                    list = LocalAudioPlayerService.Instance.PlaybackHistory.AsEnumerable();
                }
                else
                {
                    list = LocalAudioPlayerService.Instance.Playlist.AsEnumerable();
                }

                if (!string.IsNullOrWhiteSpace(_artistFilter) && _activeTab == "ALL")
                {
                    list = list.Where(x => IsTrackByArtist(x, _artistFilter));
                }

                if (!string.IsNullOrWhiteSpace(query))
                {
                    list = list.Where(x =>
                        x.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.DisplayArtist.Contains(query, StringComparison.OrdinalIgnoreCase));
                }

                // Sorting
                int sortIdx = SortComboBox?.SelectedIndex ?? 0;
                list = sortIdx switch
                {
                    1 => list.OrderByDescending(x => x.PlayCount),
                    2 => list.OrderBy(x => x.DisplayTitle),
                    3 => list.OrderBy(x => x.DisplayArtist),
                    4 => list.OrderByDescending(x => x.Duration),
                    _ => list
                };

                PlaylistListBox.ItemsSource = list.ToList();
                ClearQueueBtn.Visibility = (_activeTab == "QUEUE" && LocalAudioPlayerService.Instance.UserQueue.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // Custom Playlist Actions
        private void NewPlaylistBtn_Click(object sender, RoutedEventArgs e)
        {
            var pl = LocalAudioPlayerService.Instance.CreateCustomPlaylist($"Playlist #{LocalAudioPlayerService.Instance.CustomPlaylists.Count + 1}");
            UpdatePlaylistList();
        }

        private void DeletePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CustomPlaylist playlist)
            {
                LocalAudioPlayerService.Instance.DeleteCustomPlaylist(playlist.Id);
                _selectedCustomPlaylist = null;
                UpdateTabStyles();
                UpdatePlaylistList();
            }
        }

        private void RenamePlaylist_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is CustomPlaylist playlist)
            {
                string newName = Microsoft.VisualBasic.Interaction.InputBox("Enter new playlist name:", "Rename Playlist", playlist.Name);
                if (!string.IsNullOrWhiteSpace(newName))
                {
                    LocalAudioPlayerService.Instance.RenameCustomPlaylist(playlist.Id, newName);
                    UpdatePlaylistList();
                }
            }
        }

        private void ClearQueueBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.ClearUserQueue();
            UpdatePlaylistList();
        }

        // Context Menu Handlers
        private AudioTrackItem? GetSelectedContextTrack(object sender)
        {
            if (sender is MenuItem menuItem)
            {
                if (menuItem.DataContext is AudioTrackItem track) return track;
            }
            return PlaylistListBox.SelectedItem as AudioTrackItem;
        }

        private void ContextPlayNext_Click(object sender, RoutedEventArgs e)
        {
            var track = GetSelectedContextTrack(sender);
            if (track != null)
            {
                LocalAudioPlayerService.Instance.PlayNextInUserQueue(track);
            }
        }

        private void ContextAddToQueue_Click(object sender, RoutedEventArgs e)
        {
            var track = GetSelectedContextTrack(sender);
            if (track != null)
            {
                LocalAudioPlayerService.Instance.AddToUserQueue(track);
            }
        }

        private void ContextToggleFavorite_Click(object sender, RoutedEventArgs e)
        {
            var track = GetSelectedContextTrack(sender);
            if (track != null)
            {
                LocalAudioPlayerService.Instance.ToggleFavorite(track);
                UpdatePlaylistList();
            }
        }

        private void ContextFilterArtist_Click(object sender, RoutedEventArgs e)
        {
            var track = GetSelectedContextTrack(sender);
            if (track != null)
            {
                SearchBox.Text = track.DisplayArtist;
            }
        }

        private void ContextRemoveTrack_Click(object sender, RoutedEventArgs e)
        {
            var track = GetSelectedContextTrack(sender);
            if (track != null)
            {
                if (_activeTab == "QUEUE")
                {
                    LocalAudioPlayerService.Instance.RemoveFromUserQueue(track);
                }
                else if (_activeTab == "PLAYLISTS" && _selectedCustomPlaylist != null)
                {
                    LocalAudioPlayerService.Instance.RemoveTrackFromCustomPlaylist(_selectedCustomPlaylist.Id, track.FilePath);
                }
                UpdatePlaylistList();
            }
        }
    }
}
