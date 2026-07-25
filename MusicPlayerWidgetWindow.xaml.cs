using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KeyMapper
{
    public partial class MusicPlayerWidgetWindow : Window
    {
        private bool _isUserSeeking;
        private string _activeTab = "QUEUE"; // QUEUE, ALL, ARTISTS, FAVS
        private Storyboard? _spinStoryboard;

        public MusicPlayerWidgetWindow()
        {
            InitializeComponent();

            _spinStoryboard = (Storyboard)FindResource("SpinDiscStoryboard");

            LocalAudioPlayerService.Instance.OnTrackChanged += Instance_OnTrackChanged;
            LocalAudioPlayerService.Instance.OnPlaybackStateChanged += Instance_OnPlaybackStateChanged;
            LocalAudioPlayerService.Instance.OnPositionChanged += Instance_OnPositionChanged;
            LocalAudioPlayerService.Instance.OnFavoritesUpdated += Instance_OnFavoritesUpdated;
            LocalAudioPlayerService.Instance.OnQueueUpdated += Instance_OnQueueUpdated;

            _ = InitializeLibraryAsync();
        }

        private async System.Threading.Tasks.Task InitializeLibraryAsync()
        {
            await LocalAudioPlayerService.Instance.ScanLibraryAsync();
            UpdatePlaylistList();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void TogglePlaylistBtn_Click(object sender, RoutedEventArgs e)
        {
            if (PlaylistView.Visibility == Visibility.Visible)
            {
                PlaylistView.Visibility = Visibility.Collapsed;
                Height = 320; // Expanded safety height so controls are never clipped!
            }
            else
            {
                PlaylistView.Visibility = Visibility.Visible;
                Height = 620;
            }
        }

        private void ManageFoldersBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new ManageMusicFoldersWindow { Owner = this };
            win.ShowDialog();
            UpdatePlaylistList();
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
            ShuffleBtn.Foreground = LocalAudioPlayerService.Instance.IsShuffle ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
            UpdatePlaylistList();
        }

        private void RepeatBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.IsRepeat = !LocalAudioPlayerService.Instance.IsRepeat;
            RepeatBtn.Foreground = LocalAudioPlayerService.Instance.IsRepeat ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
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

        private void Instance_OnTrackChanged(AudioTrackItem? track)
        {
            Dispatcher.Invoke(() =>
            {
                if (track != null)
                {
                    TrackTitleTxt.Text = track.DisplayTitle;
                    TrackArtistTxt.Text = track.DisplayArtist;
                    TotalTimeTxt.Text = track.DurationText;
                    UpdateLikeButtonUI(track);

                    if (track.AlbumArt == null)
                    {
                        track.AlbumArt = LocalAudioPlayerService.Instance.ExtractEmbeddedCoverArt(track.FilePath);
                    }

                    if (track.AlbumArt != null)
                    {
                        AlbumCoverImage.Source = track.AlbumArt;
                        AlbumCoverImage.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        AlbumCoverImage.Visibility = Visibility.Collapsed;
                    }

                    UpdatePlaylistList();
                }
            });
        }

        private void UpdateLikeButtonUI(AudioTrackItem track)
        {
            LikeBtn.Content = track.IsFavorite ? "❤️" : "🤍";
            LikeBtn.Foreground = track.IsFavorite ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }

        private void Instance_OnPlaybackStateChanged(bool isPlaying)
        {
            Dispatcher.Invoke(() =>
            {
                PlayPauseBtn.Content = isPlaying ? "⏸" : "▶";
                EqualizerPanel.Visibility = isPlaying ? Visibility.Visible : Visibility.Collapsed;

                if (isPlaying)
                {
                    _spinStoryboard?.Begin();
                }
                else
                {
                    _spinStoryboard?.Pause();
                }
            });
        }

        private void Instance_OnPositionChanged(TimeSpan pos, TimeSpan total)
        {
            Dispatcher.Invoke(() =>
            {
                if (!_isUserSeeking)
                {
                    CurrentTimeTxt.Text = $"{pos:mm\\:ss}";
                    TotalTimeTxt.Text = $"{total:mm\\:ss}";
                    PositionSlider.Maximum = total.TotalSeconds > 0 ? total.TotalSeconds : 100;
                    PositionSlider.Value = pos.TotalSeconds;
                }
            });
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
            if (PlaylistListBox.SelectedItem is AudioTrackItem item && PlaylistListBox.ItemsSource is IEnumerable<AudioTrackItem> activeList)
            {
                LocalAudioPlayerService.Instance.PlayTrackItem(item, activeList);
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

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdatePlaylistList();
        }

        private void TabQueue_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "QUEUE";
            UpdateTabStyles();
            UpdatePlaylistList();
        }

        private void TabAll_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "ALL";
            UpdateTabStyles();
            UpdatePlaylistList();
        }

        private void TabArtists_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "ARTISTS";
            UpdateTabStyles();
            UpdatePlaylistList();
        }

        private void TabFavs_Click(object sender, RoutedEventArgs e)
        {
            _activeTab = "FAVS";
            UpdateTabStyles();
            UpdatePlaylistList();
        }

        private void Instance_OnFavoritesUpdated()
        {
            Dispatcher.Invoke(() => UpdatePlaylistList());
        }

        private void Instance_OnQueueUpdated()
        {
            Dispatcher.Invoke(() => UpdatePlaylistList());
        }

        private void UpdateTabStyles()
        {
            var cyan = new SolidColorBrush(Color.FromRgb(6, 182, 212));
            var dark = new SolidColorBrush(Color.FromRgb(30, 41, 59));
            var darkText = new SolidColorBrush(Color.FromRgb(15, 23, 42));
            var whiteText = new SolidColorBrush(Color.FromRgb(248, 250, 252));

            TabQueueBtn.Background = _activeTab == "QUEUE" ? cyan : dark;
            TabQueueBtn.Foreground = _activeTab == "QUEUE" ? darkText : whiteText;

            TabAllBtn.Background = _activeTab == "ALL" ? cyan : dark;
            TabAllBtn.Foreground = _activeTab == "ALL" ? darkText : whiteText;

            TabArtistsBtn.Background = _activeTab == "ARTISTS" ? cyan : dark;
            TabArtistsBtn.Foreground = _activeTab == "ARTISTS" ? darkText : whiteText;

            TabFavsBtn.Background = _activeTab == "FAVS" ? cyan : dark;
            TabFavsBtn.Foreground = _activeTab == "FAVS" ? darkText : whiteText;

            PlaylistListBox.Visibility = _activeTab == "ARTISTS" ? Visibility.Collapsed : Visibility.Visible;
            ArtistsListBox.Visibility = _activeTab == "ARTISTS" ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePlaylistList()
        {
            string query = SearchBox.Text?.Trim() ?? string.Empty;

            if (_activeTab == "ARTISTS")
            {
                var artists = LocalAudioPlayerService.Instance.ArtistGroups;
                if (string.IsNullOrWhiteSpace(query))
                {
                    ArtistsListBox.ItemsSource = artists;
                }
                else
                {
                    ArtistsListBox.ItemsSource = artists.Where(a => a.ArtistName.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
                }
            }
            else
            {
                IEnumerable<AudioTrackItem> list;

                if (_activeTab == "QUEUE")
                {
                    list = LocalAudioPlayerService.Instance.ActivePlaybackQueue.AsEnumerable();
                }
                else if (_activeTab == "FAVS")
                {
                    list = LocalAudioPlayerService.Instance.Playlist.Where(x => x.IsFavorite);
                }
                else
                {
                    list = LocalAudioPlayerService.Instance.Playlist.AsEnumerable();
                }

                if (!string.IsNullOrWhiteSpace(query))
                {
                    list = list.Where(x =>
                        x.DisplayTitle.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                        x.DisplayArtist.Contains(query, StringComparison.OrdinalIgnoreCase));
                }

                var sortedList = list.ToList();
                PlaylistListBox.ItemsSource = sortedList;
            }
        }
    }
}
