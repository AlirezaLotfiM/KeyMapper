using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyMapper
{
    public class AudioTrackItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public BitmapSource? AlbumArt { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsCurrentlyPlaying { get; set; }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(FilePath) : Title;
        public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;
        public string DurationText => Duration.TotalSeconds > 0 ? $"{Duration:mm\\:ss}" : "--:--";
        public string FavoriteIcon => IsFavorite ? "❤️" : "🤍";
        public Brush FavoriteColor => IsFavorite ? new SolidColorBrush(Color.FromRgb(239, 68, 68)) : new SolidColorBrush(Color.FromRgb(148, 163, 184));
        public Brush TitleBrush => IsCurrentlyPlaying ? new SolidColorBrush(Color.FromRgb(6, 182, 212)) : new SolidColorBrush(Color.FromRgb(248, 250, 252));
        public Brush CardBackground => IsCurrentlyPlaying ? new SolidColorBrush(Color.FromArgb(50, 6, 182, 212)) : new SolidColorBrush(Colors.Transparent);
    }

    public class ArtistGroupItem
    {
        public string ArtistName { get; set; } = "Unknown Artist";
        public int TrackCount { get; set; }
        public List<AudioTrackItem> Tracks { get; set; } = new();
    }

    public sealed class LocalAudioPlayerService
    {
        private static readonly Lazy<LocalAudioPlayerService> LazyInstance = new(() => new LocalAudioPlayerService());
        public static LocalAudioPlayerService Instance => LazyInstance.Value;

        private readonly MediaPlayer _mediaPlayer = new();
        private readonly DispatcherTimer _positionTimer;
        private readonly List<string> _musicFolders = new();
        private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);

        public ObservableCollection<AudioTrackItem> Playlist { get; } = new();
        public ObservableCollection<ArtistGroupItem> ArtistGroups { get; } = new();

        public List<AudioTrackItem> ActivePlaybackQueue { get; private set; } = new();

        private int _currentIndex = -1;
        public AudioTrackItem? CurrentTrack => (_currentIndex >= 0 && _currentIndex < ActivePlaybackQueue.Count) ? ActivePlaybackQueue[_currentIndex] : null;

        public bool IsPlaying { get; private set; }
        public bool IsShuffle { get; set; }
        public bool IsRepeat { get; set; }

        public event Action<AudioTrackItem?>? OnTrackChanged;
        public event Action<bool>? OnPlaybackStateChanged;
        public event Action<TimeSpan, TimeSpan>? OnPositionChanged;
        public event Action? OnFavoritesUpdated;
        public event Action? OnQueueUpdated;

        private readonly string _favoritesFilePath;
        private readonly string _foldersFilePath;

        private LocalAudioPlayerService()
        {
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _positionTimer.Tick += PositionTimer_Tick;

            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyMapper");
            Directory.CreateDirectory(appData);
            _favoritesFilePath = Path.Combine(appData, "music_favorites.json");
            _foldersFilePath = Path.Combine(appData, "music_folders.json");

            LoadSavedData();
        }

        private void LoadSavedData()
        {
            if (File.Exists(_foldersFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_foldersFilePath);
                    var list = JsonSerializer.Deserialize<List<string>>(json);
                    if (list != null)
                    {
                        foreach (var f in list) if (Directory.Exists(f)) _musicFolders.Add(f);
                    }
                }
                catch { }
            }

            if (_musicFolders.Count == 0 && Directory.Exists(@"E:\Sandbox\Spotist\downloads"))
            {
                _musicFolders.Add(@"E:\Sandbox\Spotist\downloads");
            }

            if (File.Exists(_favoritesFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_favoritesFilePath);
                    var favs = JsonSerializer.Deserialize<List<string>>(json);
                    if (favs != null)
                    {
                        foreach (var f in favs) _favoritePaths.Add(f);
                    }
                }
                catch { }
            }
        }

        public void SaveFolders()
        {
            try
            {
                string json = JsonSerializer.Serialize(_musicFolders);
                File.WriteAllText(_foldersFilePath, json);
            }
            catch { }
        }

        public void SaveFavorites()
        {
            try
            {
                string json = JsonSerializer.Serialize(_favoritePaths.ToList());
                File.WriteAllText(_favoritesFilePath, json);
            }
            catch { }
        }

        public List<string> GetFolders() => new(_musicFolders);

        public void AddFolder(string folderPath)
        {
            if (Directory.Exists(folderPath) && !_musicFolders.Contains(folderPath, StringComparer.OrdinalIgnoreCase))
            {
                _musicFolders.Add(folderPath);
                SaveFolders();
            }
        }

        public void RemoveFolder(string folderPath)
        {
            _musicFolders.RemoveAll(f => string.Equals(f, folderPath, StringComparison.OrdinalIgnoreCase));
            SaveFolders();
        }

        public void ToggleFavorite(AudioTrackItem track)
        {
            if (track == null) return;
            track.IsFavorite = !track.IsFavorite;

            if (track.IsFavorite)
            {
                _favoritePaths.Add(track.FilePath);
            }
            else
            {
                _favoritePaths.Remove(track.FilePath);
            }
            SaveFavorites();
            OnFavoritesUpdated?.Invoke();
        }

        public void ToggleShuffle()
        {
            IsShuffle = !IsShuffle;
            if (IsShuffle && ActivePlaybackQueue.Count > 1)
            {
                var current = CurrentTrack;
                var list = ActivePlaybackQueue.Where(x => x != current).OrderBy(_ => Random.Shared.Next()).ToList();
                if (current != null) list.Insert(0, current);
                ActivePlaybackQueue = list;
                _currentIndex = 0;
                OnQueueUpdated?.Invoke();
            }
        }

        public async Task ScanLibraryAsync()
        {
            await Task.Run(() =>
            {
                List<string> files = new();
                string[] extensions = { ".mp3", ".flac", ".m4a", ".wav", ".wma", ".aac" };

                foreach (var folder in _musicFolders)
                {
                    try
                    {
                        if (Directory.Exists(folder))
                        {
                            var foundFiles = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                                .Where(f => extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
                            files.AddRange(foundFiles);
                        }
                    }
                    catch { }
                }

                List<AudioTrackItem> items = new();

                foreach (var file in files)
                {
                    try
                    {
                        var item = ParseNativeId3v2Tags(file);
                        item.IsFavorite = _favoritePaths.Contains(file);
                        // Extract Thumbnail Art for first 100 items immediately
                        if (items.Count < 100)
                        {
                            item.AlbumArt = ExtractEmbeddedCoverArt(file);
                        }
                        items.Add(item);
                    }
                    catch { }
                }

                var artistDict = new Dictionary<string, List<AudioTrackItem>>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    string rawArtists = item.DisplayArtist;
                    var splitArtists = rawArtists.Split(new[] { ';', ',', '&', '/', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                        .SelectMany(a => a.Split(new[] { " ft. ", " FEAT. ", " feat. ", " WITH ", " with ", " AND ", " and " }, StringSplitOptions.RemoveEmptyEntries))
                        .Select(a => a.Trim())
                        .Where(a => !string.IsNullOrWhiteSpace(a) && !a.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (splitArtists.Count == 0) splitArtists.Add(rawArtists);

                    foreach (var artist in splitArtists)
                    {
                        if (!artistDict.TryGetValue(artist, out var list))
                        {
                            list = new List<AudioTrackItem>();
                            artistDict[artist] = list;
                        }
                        list.Add(item);
                    }
                }

                var groups = artistDict.Select(kvp => new ArtistGroupItem
                {
                    ArtistName = kvp.Key,
                    TrackCount = kvp.Value.Count,
                    Tracks = kvp.Value
                })
                .OrderBy(g => g.ArtistName)
                .ToList();

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Playlist.Clear();
                    foreach (var it in items) Playlist.Add(it);

                    ArtistGroups.Clear();
                    foreach (var grp in groups) ArtistGroups.Add(grp);

                    ActivePlaybackQueue = Playlist.ToList();
                    if (ActivePlaybackQueue.Count > 0 && _currentIndex < 0) _currentIndex = 0;
                });
            });
        }

        public void PlayTrack(int index)
        {
            if (index < 0 || index >= ActivePlaybackQueue.Count) return;
            
            foreach (var item in Playlist) item.IsCurrentlyPlaying = false;
            
            _currentIndex = index;
            var track = ActivePlaybackQueue[_currentIndex];
            track.IsCurrentlyPlaying = true;

            try
            {
                if (track.AlbumArt == null)
                {
                    track.AlbumArt = ExtractEmbeddedCoverArt(track.FilePath);
                }

                _mediaPlayer.Open(new Uri(track.FilePath));
                _mediaPlayer.Play();
                IsPlaying = true;
                _positionTimer.Start();

                OnTrackChanged?.Invoke(track);
                OnPlaybackStateChanged?.Invoke(true);
            }
            catch { }
        }

        public void PlayTrackItem(AudioTrackItem track, IEnumerable<AudioTrackItem>? currentContextList = null)
        {
            if (currentContextList != null)
            {
                ActivePlaybackQueue = currentContextList.ToList();
            }

            int idx = ActivePlaybackQueue.IndexOf(track);
            if (idx >= 0)
            {
                PlayTrack(idx);
            }
        }

        public void TogglePlayPause()
        {
            if (ActivePlaybackQueue.Count == 0) return;
            if (_currentIndex < 0) _currentIndex = 0;

            if (IsPlaying)
            {
                _mediaPlayer.Pause();
                IsPlaying = false;
                _positionTimer.Stop();
                OnPlaybackStateChanged?.Invoke(false);
            }
            else
            {
                if (CurrentTrack != null)
                {
                    _mediaPlayer.Play();
                    IsPlaying = true;
                    _positionTimer.Start();
                    OnPlaybackStateChanged?.Invoke(true);
                }
                else
                {
                    PlayTrack(_currentIndex);
                }
            }
        }

        public void PlayNext()
        {
            if (ActivePlaybackQueue.Count == 0) return;
            int nextIndex = (_currentIndex + 1) % ActivePlaybackQueue.Count;
            PlayTrack(nextIndex);
        }

        public void PlayPrevious()
        {
            if (ActivePlaybackQueue.Count == 0) return;
            int prevIndex = (_currentIndex - 1 + ActivePlaybackQueue.Count) % ActivePlaybackQueue.Count;
            PlayTrack(prevIndex);
        }

        public void Seek(double positionSeconds)
        {
            _mediaPlayer.Position = TimeSpan.FromSeconds(positionSeconds);
        }

        public void SetVolume(double volumePercent)
        {
            _mediaPlayer.Volume = Math.Clamp(volumePercent / 100.0, 0.0, 1.0);
        }

        private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan && CurrentTrack != null)
            {
                CurrentTrack.Duration = _mediaPlayer.NaturalDuration.TimeSpan;
            }
        }

        private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
        {
            if (CurrentTrack != null && ActivePlaybackQueue.Count > 0)
            {
                var finishedTrack = CurrentTrack;
                if (IsRepeat)
                {
                    // Move finished track to end of active queue
                    ActivePlaybackQueue.Remove(finishedTrack);
                    ActivePlaybackQueue.Add(finishedTrack);
                    PlayTrack(0);
                }
                else
                {
                    // Remove finished track from Now Playing Queue
                    ActivePlaybackQueue.Remove(finishedTrack);
                    if (ActivePlaybackQueue.Count > 0)
                    {
                        PlayTrack(0);
                    }
                    else
                    {
                        IsPlaying = false;
                        OnPlaybackStateChanged?.Invoke(false);
                    }
                }
                OnQueueUpdated?.Invoke();
            }
        }

        private void PositionTimer_Tick(object? sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan)
            {
                OnPositionChanged?.Invoke(_mediaPlayer.Position, _mediaPlayer.NaturalDuration.TimeSpan);
            }
        }

        public BitmapSource? ExtractEmbeddedCoverArt(string filePath)
        {
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] buffer = new byte[Math.Min(fs.Length, 1024 * 1024)];
                int read = fs.Read(buffer, 0, buffer.Length);

                int jpegHeader = -1;
                for (int i = 0; i < read - 3; i++)
                {
                    if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
                    {
                        jpegHeader = i;
                        break;
                    }
                }

                if (jpegHeader >= 0)
                {
                    using var ms = new MemoryStream(buffer, jpegHeader, read - jpegHeader);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { }

            return null;
        }

        private AudioTrackItem ParseNativeId3v2Tags(string filePath)
        {
            var item = new AudioTrackItem
            {
                FilePath = filePath,
                Title = Path.GetFileNameWithoutExtension(filePath)
            };

            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                byte[] header = new byte[10];
                if (fs.Read(header, 0, 10) == 10 && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3')
                {
                    int tagSize = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
                    byte[] tagBytes = new byte[tagSize];
                    int read = fs.Read(tagBytes, 0, tagSize);

                    string title = string.Empty;
                    List<string> artists = new();

                    int idx = 0;
                    while (idx < read - 10)
                    {
                        string frameId = Encoding.ASCII.GetString(tagBytes, idx, 4);
                        if (!char.IsLetterOrDigit(frameId[0])) { idx++; continue; }

                        int frameSize = (tagBytes[idx + 4] << 24) | (tagBytes[idx + 5] << 16) | (tagBytes[idx + 6] << 8) | tagBytes[idx + 7];
                        if (frameSize <= 0 || frameSize > read - idx - 10) { idx++; continue; }

                        if (frameId == "TIT2" || frameId == "TPE1" || frameId == "TPE2")
                        {
                            string text = DecodeId3Text(tagBytes, idx + 10, frameSize);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                if (frameId == "TIT2") title = text;
                                else if (frameId == "TPE1" || frameId == "TPE2") artists.Add(text);
                            }
                        }

                        idx += 10 + frameSize;
                    }

                    if (!string.IsNullOrWhiteSpace(title)) item.Title = title;
                    if (artists.Count > 0)
                    {
                        var cleanArtists = artists.SelectMany(a => a.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                                                   .Select(a => a.Trim())
                                                   .Where(a => !string.IsNullOrWhiteSpace(a))
                                                   .Distinct(StringComparer.OrdinalIgnoreCase);
                        item.Artist = string.Join("; ", cleanArtists);
                    }
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(item.Artist) || item.Artist == "Unknown Artist")
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (fileName.Contains(" - "))
                {
                    var parts = fileName.Split(new[] { " - " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2)
                    {
                        item.Artist = parts[0].Trim();
                        item.Title = parts[1].Trim();
                    }
                }
                else if (fileName.Contains("-"))
                {
                    var parts = fileName.Split('-', 2);
                    item.Artist = parts[0].Trim();
                    item.Title = parts[1].Trim();
                }
            }

            return item;
        }

        private string DecodeId3Text(byte[] buffer, int start, int length)
        {
            try
            {
                if (length <= 1) return string.Empty;
                byte encoding = buffer[start];

                if (encoding == 1 && length >= 3)
                {
                    return Encoding.Unicode.GetString(buffer, start + 3, length - 3).Trim('\0', '\r', '\n', '\t');
                }
                else if (encoding == 3 || encoding == 0)
                {
                    return Encoding.UTF8.GetString(buffer, start + 1, length - 1).Trim('\0', '\r', '\n', '\t');
                }
                else
                {
                    return Encoding.Default.GetString(buffer, start + 1, length - 1).Trim('\0', '\r', '\n', '\t');
                }
            }
            catch { }
            return string.Empty;
        }
    }
}
