using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyMapper
{
    public enum RepeatMode
    {
        Off,
        RepeatAll,
        RepeatOne
    }

    public class AudioTrackItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Genre { get; set; } = "General Music";
        public TimeSpan Duration { get; set; }
        public BitmapSource? AlbumArt { get; set; }
        public bool IsFavorite { get; set; }
        public bool IsCurrentlyPlaying { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
        public int PlayCount { get; set; }
        public DateTime LastPlayedAt { get; set; }

        public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? Path.GetFileNameWithoutExtension(FilePath) : Title;
        public string DisplayArtist => string.IsNullOrWhiteSpace(Artist) ? "Unknown Artist" : Artist;
        public string DurationText => Duration.TotalSeconds > 0 ? $"{Duration:mm\\:ss}" : "--:--";
    }

    public class ArtistGroupItem
    {
        public string ArtistName { get; set; } = "Unknown Artist";
        public int TrackCount { get; set; }
        public List<AudioTrackItem> Tracks { get; set; } = new();
        public BitmapSource? ArtistArt => Tracks.FirstOrDefault(t => t.AlbumArt != null)?.AlbumArt;
    }

    public class GenreGroupItem
    {
        public string GenreName { get; set; } = "General Music";
        public int TrackCount => Tracks.Count;
        public List<AudioTrackItem> Tracks { get; set; } = new();
        public BitmapSource? GenreArt => Tracks.FirstOrDefault(t => t.AlbumArt != null)?.AlbumArt;
    }

    public class CustomPlaylist
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<string> TrackFilePaths { get; set; } = new();
        public int TrackCount => TrackFilePaths.Count;
    }

    public class SavedPlaybackSession
    {
        public string LastTrackFilePath { get; set; } = string.Empty;
        public double PositionSeconds { get; set; }
        public double VolumePercent { get; set; } = 80;
        public bool IsShuffle { get; set; }
        public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
        public List<string> UserQueuePaths { get; set; } = new();
    }

    public class CachedTrackItem
    {
        public string FilePath { get; set; } = string.Empty;
        public DateTime LastWriteTimeUtc { get; set; }
        public long FileSizeBytes { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Album { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public string CoverArtFileName { get; set; } = string.Empty;
    }

    public sealed class LocalAudioPlayerService
    {
        private static readonly Lazy<LocalAudioPlayerService> LazyInstance = new(() => new LocalAudioPlayerService());
        public static LocalAudioPlayerService Instance => LazyInstance.Value;

        private readonly MediaPlayer _mediaPlayer = new();
        private readonly DispatcherTimer _positionTimer;
        private readonly List<string> _musicFolders = new();
        private readonly HashSet<string> _favoritePaths = new(StringComparer.OrdinalIgnoreCase);

        // Core Collections
        public ObservableCollection<AudioTrackItem> Playlist { get; } = new();
        public ObservableCollection<ArtistGroupItem> ArtistGroups { get; } = new();
        public ObservableCollection<GenreGroupItem> GenreGroups { get; } = new();
        public ObservableCollection<CustomPlaylist> CustomPlaylists { get; } = new();
        public ObservableCollection<AudioTrackItem> PlaybackHistory { get; } = new();

        // 2-Tier Queue Architecture
        public ObservableCollection<AudioTrackItem> UserQueue { get; } = new();
        public List<AudioTrackItem> ContextQueue { get; private set; } = new();
        private List<AudioTrackItem> _unshuffledContextQueue = new();

        private SavedPlaybackSession? _savedSession;
        private double _pendingSeekPositionSeconds = -1;
        // True once the saved session (last track + queue) has been applied after first scan.
        private bool _sessionRestored = false;

        public List<AudioTrackItem> ActivePlaybackQueue
        {
            get
            {
                var combined = new List<AudioTrackItem>();
                if (CurrentTrack != null && !UserQueue.Contains(CurrentTrack) && !ContextQueue.Contains(CurrentTrack))
                {
                    combined.Add(CurrentTrack);
                }
                combined.AddRange(UserQueue);
                combined.AddRange(ContextQueue);
                return combined.Distinct().ToList();
            }
        }

        private int _contextIndex = -1;
        public AudioTrackItem? CurrentTrack { get; private set; }

        public bool IsPlaying { get; private set; }
        public bool IsShuffle { get; private set; }
        public RepeatMode RepeatMode { get; set; } = RepeatMode.Off;
        public double CurrentVolume { get; private set; } = 0.8;
        public TimeSpan CurrentPosition => _mediaPlayer.Position;

        public event Action<AudioTrackItem?>? OnTrackChanged;
        public event Action<bool>? OnPlaybackStateChanged;
        public event Action<TimeSpan, TimeSpan>? OnPositionChanged;
        public event Action<double>? OnVolumeChanged;
        public event Action? OnFavoritesUpdated;
        public event Action? OnQueueUpdated;
        public event Action? OnCustomPlaylistsUpdated;
        public event Action? OnHistoryUpdated;
        public event Action? OnPlayCountsUpdated;

        private readonly string _appDataDir;
        private readonly string _favoritesFilePath;
        private readonly string _foldersFilePath;
        private readonly string _playlistsFilePath;
        private readonly string _historyFilePath;
        private readonly string _sessionFilePath;
        private readonly string _playCountsFilePath;
        private readonly string _cacheFilePath;
        private readonly string _coverArtCacheDir;
        private readonly Dictionary<string, CachedTrackItem> _trackCache = new(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> _playCounts = new(StringComparer.OrdinalIgnoreCase);

        private LocalAudioPlayerService()
        {
            _mediaPlayer.MediaEnded += MediaPlayer_MediaEnded;
            _mediaPlayer.MediaOpened += MediaPlayer_MediaOpened;

            _positionTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _positionTimer.Tick += PositionTimer_Tick;

            _appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyMapper");
            Directory.CreateDirectory(_appDataDir);

            _favoritesFilePath = Path.Combine(_appDataDir, "music_favorites.json");
            _foldersFilePath = Path.Combine(_appDataDir, "music_folders.json");
            _playlistsFilePath = Path.Combine(_appDataDir, "custom_playlists.json");
            _historyFilePath = Path.Combine(_appDataDir, "playback_history.json");
            _sessionFilePath = Path.Combine(_appDataDir, "music_session.json");
            _playCountsFilePath = Path.Combine(_appDataDir, "play_counts.json");
            _cacheFilePath = Path.Combine(_appDataDir, "music_cache.json");
            _coverArtCacheDir = Path.Combine(_appDataDir, "CoverArtCache");
            try
            {
                Directory.CreateDirectory(_coverArtCacheDir);
            }
            catch { }

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

            if (File.Exists(_playlistsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_playlistsFilePath);
                    var playlists = JsonSerializer.Deserialize<List<CustomPlaylist>>(json);
                    if (playlists != null)
                    {
                        CustomPlaylists.Clear();
                        foreach (var p in playlists) CustomPlaylists.Add(p);
                    }
                }
                catch { }
            }

            if (File.Exists(_sessionFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_sessionFilePath);
                    _savedSession = JsonSerializer.Deserialize<SavedPlaybackSession>(json);
                    if (_savedSession != null)
                    {
                        IsShuffle = _savedSession.IsShuffle;
                        RepeatMode = _savedSession.RepeatMode;
                        SetVolume(_savedSession.VolumePercent);
                    }
                }
                catch { }
            }

            if (File.Exists(_playCountsFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_playCountsFilePath);
                    var dict = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
                    if (dict != null) _playCounts = new Dictionary<string, int>(dict, StringComparer.OrdinalIgnoreCase);
                }
                catch { }
            }

            LoadTrackCache();
            BuildInitialPlaylistFromCache();
        }

        private void LoadTrackCache()
        {
            if (File.Exists(_cacheFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_cacheFilePath);
                    var list = JsonSerializer.Deserialize<List<CachedTrackItem>>(json);
                    if (list != null)
                    {
                        _trackCache.Clear();
                        foreach (var item in list)
                        {
                            if (!string.IsNullOrWhiteSpace(item.FilePath))
                            {
                                _trackCache[item.FilePath] = item;
                            }
                        }
                    }
                }
                catch { }
            }
        }

        private void SaveTrackCache()
        {
            try
            {
                string json = JsonSerializer.Serialize(_trackCache.Values.ToList());
                File.WriteAllText(_cacheFilePath, json);
            }
            catch { }
        }

        private void BuildInitialPlaylistFromCache()
        {
            if (_trackCache.Count == 0) return;

            var items = new List<AudioTrackItem>();
            foreach (var kvp in _trackCache)
            {
                var cached = kvp.Value;
                if (!File.Exists(cached.FilePath)) continue;

                var track = new AudioTrackItem
                {
                    FilePath = cached.FilePath,
                    Title = cached.Title,
                    Artist = cached.Artist,
                    Album = cached.Album,
                    Genre = string.IsNullOrWhiteSpace(cached.Genre) ? "General Music" : cached.Genre,
                    Duration = TimeSpan.FromSeconds(cached.DurationSeconds),
                    IsFavorite = _favoritePaths.Contains(cached.FilePath),
                    AlbumArt = LoadCoverArtFromCache(cached.CoverArtFileName)
                };
                if (_playCounts.TryGetValue(cached.FilePath, out int count))
                {
                    track.PlayCount = count;
                }
                items.Add(track);
            }

            if (items.Count == 0) return;

            Playlist.Clear();
            foreach (var it in items) Playlist.Add(it);

            // Group by Artist & Genre
            PopulateGroupsAndQueues(items);
        }

        private BitmapSource? LoadCoverArtFromCache(string artFileName)
        {
            if (string.IsNullOrWhiteSpace(artFileName)) return null;
            string fullPath = Path.Combine(_coverArtCacheDir, artFileName);
            if (!File.Exists(fullPath)) return null;
            try
            {
                using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 100;
                bitmap.StreamSource = fs;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { }
            return null;
        }

        private string SaveCoverArtToCache(string filePath, BitmapSource? art)
        {
            if (art == null) return string.Empty;
            try
            {
                byte[] hashBytes = System.Security.Cryptography.MD5.HashData(Encoding.UTF8.GetBytes(filePath.ToLowerInvariant()));
                string artFileName = $"{Convert.ToHexString(hashBytes)}.png";
                string artFullPath = Path.Combine(_coverArtCacheDir, artFileName);
                if (File.Exists(artFullPath)) return artFileName;

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(art));
                using var stream = File.Create(artFullPath);
                encoder.Save(stream);
                return artFileName;
            }
            catch { }
            return string.Empty;
        }

        private void PopulateGroupsAndQueues(List<AudioTrackItem> items)
        {
            var artistDict = new Dictionary<string, List<AudioTrackItem>>(StringComparer.OrdinalIgnoreCase);
            var genreDict = new Dictionary<string, List<AudioTrackItem>>(StringComparer.OrdinalIgnoreCase);

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

                string genreName = NormalizeGenre(item.Genre);
                if (!genreDict.TryGetValue(genreName, out var gList))
                {
                    gList = new List<AudioTrackItem>();
                    genreDict[genreName] = gList;
                }
                gList.Add(item);
            }

            ArtistGroups.Clear();
            foreach (var grp in artistDict.Select(kvp => new ArtistGroupItem { ArtistName = kvp.Key, TrackCount = kvp.Value.Count, Tracks = kvp.Value }).OrderBy(g => g.ArtistName))
            {
                ArtistGroups.Add(grp);
            }

            GenreGroups.Clear();
            foreach (var gGrp in genreDict.Select(kvp => new GenreGroupItem { GenreName = kvp.Key, Tracks = kvp.Value }).OrderBy(g => g.GenreName))
            {
                GenreGroups.Add(gGrp);
            }

            _unshuffledContextQueue = Playlist.ToList();
            ContextQueue = Playlist.ToList();
        }

        public void SavePlayCounts()
        {
            try
            {
                string json = JsonSerializer.Serialize(_playCounts);
                File.WriteAllText(_playCountsFilePath, json);
            }
            catch { }
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

        public void SaveCustomPlaylists()
        {
            try
            {
                string json = JsonSerializer.Serialize(CustomPlaylists.ToList());
                File.WriteAllText(_playlistsFilePath, json);
            }
            catch { }
        }

        public void SaveHistory()
        {
            try
            {
                var paths = PlaybackHistory.Select(x => x.FilePath).Take(30).ToList();
                string json = JsonSerializer.Serialize(paths);
                File.WriteAllText(_historyFilePath, json);
            }
            catch { }
        }

        public void SaveSession()
        {
            try
            {
                var session = new SavedPlaybackSession
                {
                    LastTrackFilePath = CurrentTrack?.FilePath ?? string.Empty,
                    PositionSeconds = CurrentPosition.TotalSeconds,
                    VolumePercent = CurrentVolume * 100.0,
                    IsShuffle = IsShuffle,
                    RepeatMode = RepeatMode,
                    UserQueuePaths = UserQueue.Select(x => x.FilePath).ToList()
                };
                string json = JsonSerializer.Serialize(session);
                File.WriteAllText(_sessionFilePath, json);
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

        // Two-Tier User Queue Actions
        public void PlayNextInUserQueue(AudioTrackItem track)
        {
            if (track == null) return;
            UserQueue.Remove(track);
            UserQueue.Insert(0, track);
            OnQueueUpdated?.Invoke();
            SaveSession();
        }

        public void AddToUserQueue(AudioTrackItem track)
        {
            if (track == null) return;
            if (!UserQueue.Contains(track))
            {
                UserQueue.Add(track);
            }
            OnQueueUpdated?.Invoke();
            SaveSession();
        }

        public void RemoveFromUserQueue(AudioTrackItem track)
        {
            if (track == null) return;
            bool u = UserQueue.Remove(track);
            bool c = ContextQueue.Remove(track);
            _unshuffledContextQueue.Remove(track);
            if (u || c)
            {
                OnQueueUpdated?.Invoke();
                SaveSession();
            }
        }

        public void ClearUserQueue()
        {
            UserQueue.Clear();
            OnQueueUpdated?.Invoke();
            SaveSession();
        }

        // Custom Playlists Operations
        public CustomPlaylist CreateCustomPlaylist(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) name = "New Playlist";
            var pl = new CustomPlaylist { Name = name.Trim() };
            CustomPlaylists.Add(pl);
            SaveCustomPlaylists();
            OnCustomPlaylistsUpdated?.Invoke();
            return pl;
        }

        public void DeleteCustomPlaylist(string playlistId)
        {
            var pl = CustomPlaylists.FirstOrDefault(p => p.Id == playlistId);
            if (pl != null)
            {
                CustomPlaylists.Remove(pl);
                SaveCustomPlaylists();
                OnCustomPlaylistsUpdated?.Invoke();
            }
        }

        public void RenameCustomPlaylist(string playlistId, string newName)
        {
            var pl = CustomPlaylists.FirstOrDefault(p => p.Id == playlistId);
            if (pl != null && !string.IsNullOrWhiteSpace(newName))
            {
                pl.Name = newName.Trim();
                SaveCustomPlaylists();
                OnCustomPlaylistsUpdated?.Invoke();
            }
        }

        public void AddTrackToCustomPlaylist(string playlistId, AudioTrackItem track)
        {
            var pl = CustomPlaylists.FirstOrDefault(p => p.Id == playlistId);
            if (pl != null && track != null && !pl.TrackFilePaths.Contains(track.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                pl.TrackFilePaths.Add(track.FilePath);
                SaveCustomPlaylists();
                OnCustomPlaylistsUpdated?.Invoke();
            }
        }

        public void RemoveTrackFromCustomPlaylist(string playlistId, string filePath)
        {
            var pl = CustomPlaylists.FirstOrDefault(p => p.Id == playlistId);
            if (pl != null)
            {
                pl.TrackFilePaths.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
                SaveCustomPlaylists();
                OnCustomPlaylistsUpdated?.Invoke();
            }
        }

        public List<AudioTrackItem> GetCustomPlaylistTracks(CustomPlaylist playlist)
        {
            if (playlist == null) return new List<AudioTrackItem>();
            var pathSet = new HashSet<string>(playlist.TrackFilePaths, StringComparer.OrdinalIgnoreCase);
            return Playlist.Where(t => pathSet.Contains(t.FilePath)).ToList();
        }

        public void ToggleShuffle()
        {
            IsShuffle = !IsShuffle;
            if (ContextQueue.Count > 0)
            {
                if (IsShuffle)
                {
                    _unshuffledContextQueue = new List<AudioTrackItem>(ContextQueue);
                    var current = CurrentTrack;
                    var shuffled = ContextQueue.Where(x => x != current).OrderBy(_ => Random.Shared.Next()).ToList();
                    if (current != null) shuffled.Insert(0, current);
                    ContextQueue = shuffled;
                    _contextIndex = CurrentTrack != null ? ContextQueue.IndexOf(CurrentTrack) : 0;
                }
                else
                {
                    if (_unshuffledContextQueue.Count > 0)
                    {
                        ContextQueue = new List<AudioTrackItem>(_unshuffledContextQueue);
                        _contextIndex = CurrentTrack != null ? ContextQueue.IndexOf(CurrentTrack) : 0;
                    }
                }
            }
            OnQueueUpdated?.Invoke();
            SaveSession();
        }

        public void CycleRepeatMode()
        {
            RepeatMode = RepeatMode switch
            {
                RepeatMode.Off => RepeatMode.RepeatAll,
                RepeatMode.RepeatAll => RepeatMode.RepeatOne,
                RepeatMode.RepeatOne => RepeatMode.Off,
                _ => RepeatMode.Off
            };
            SaveSession();
        }

        public async Task ScanLibraryAsync()
        {
            AudioTrackItem? activeTrackBeforeScan = CurrentTrack;
            string activePath = CurrentTrack?.FilePath ?? string.Empty;
            // isInitialLibraryLoad must be based on whether we have ever restored the session,
            // NOT on Playlist.Count (which is already populated by BuildInitialPlaylistFromCache).
            bool isInitialLibraryLoad = !_sessionRestored;
            List<string> queuedPaths = UserQueue
                .Select(track => track.FilePath)
                .Concat(
                    isInitialLibraryLoad
                        ? _savedSession?.UserQueuePaths ??
                          Enumerable.Empty<string>()
                        : Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            bool preserveActiveStream =
                !string.IsNullOrWhiteSpace(activePath) &&
                _mediaPlayer.Source != null;
            bool wasPlaying = IsPlaying;
            TimeSpan activePosition =
                preserveActiveStream ? _mediaPlayer.Position : TimeSpan.Zero;
            TimeSpan activeDuration =
                CurrentTrack?.Duration ?? TimeSpan.Zero;

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

                files = files
                    .Select(Path.GetFullPath)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(path =>
                        string.Equals(
                            path,
                            activePath,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                List<AudioTrackItem> items = new();
                var seenTracks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var currentFoundFiles = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);

                foreach (var file in files)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        AudioTrackItem item;
                        if (_trackCache.TryGetValue(file, out var cached) &&
                            cached.LastWriteTimeUtc == fi.LastWriteTimeUtc &&
                            cached.FileSizeBytes == fi.Length)
                        {
                            item = new AudioTrackItem
                            {
                                FilePath = cached.FilePath,
                                Title = cached.Title,
                                Artist = cached.Artist,
                                Album = cached.Album,
                                Genre = string.IsNullOrWhiteSpace(cached.Genre) ? "General Music" : cached.Genre,
                                Duration = TimeSpan.FromSeconds(cached.DurationSeconds),
                                IsFavorite = _favoritePaths.Contains(cached.FilePath),
                                AlbumArt = LoadCoverArtFromCache(cached.CoverArtFileName)
                            };
                        }
                        else
                        {
                            item = ParseNativeId3v2Tags(file);
                            item.IsFavorite = _favoritePaths.Contains(file);
                            item.AlbumArt = ExtractEmbeddedCoverArt(file);

                            string artFileName = SaveCoverArtToCache(file, item.AlbumArt);
                            _trackCache[file] = new CachedTrackItem
                            {
                                FilePath = file,
                                LastWriteTimeUtc = fi.LastWriteTimeUtc,
                                FileSizeBytes = fi.Length,
                                Title = item.Title,
                                Artist = item.Artist,
                                Album = item.Album,
                                Genre = item.Genre,
                                DurationSeconds = item.Duration.TotalSeconds,
                                CoverArtFileName = artFileName
                            };
                        }

                        if (_playCounts.TryGetValue(file, out int count))
                        {
                            item.PlayCount = count;
                        }
                        if (!seenTracks.Add(GetTrackIdentity(item)))
                        {
                            continue;
                        }
                        items.Add(item);
                    }
                    catch { }
                }

                // Remove files from cache that no longer exist
                var removedKeys = _trackCache.Keys.Where(k => !currentFoundFiles.Contains(k)).ToList();
                foreach (var rk in removedKeys)
                {
                    _trackCache.Remove(rk);
                }
                SaveTrackCache();

                // Group by Artist
                var artistDict = new Dictionary<string, List<AudioTrackItem>>(StringComparer.OrdinalIgnoreCase);
                var genreDict = new Dictionary<string, List<AudioTrackItem>>(StringComparer.OrdinalIgnoreCase);

                foreach (var item in items)
                {
                    // Artist Grouping
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

                    // Genre Grouping
                    string genreName = NormalizeGenre(item.Genre);
                    if (!genreDict.TryGetValue(genreName, out var gList))
                    {
                        gList = new List<AudioTrackItem>();
                        genreDict[genreName] = gList;
                    }
                    gList.Add(item);
                }

                var artistGroups = artistDict.Select(kvp => new ArtistGroupItem
                {
                    ArtistName = kvp.Key,
                    TrackCount = kvp.Value.Count,
                    Tracks = kvp.Value
                })
                .OrderBy(g => g.ArtistName)
                .ToList();

                var genreGroups = genreDict.Select(kvp => new GenreGroupItem
                {
                    GenreName = kvp.Key,
                    Tracks = kvp.Value
                })
                .OrderBy(g => g.GenreName)
                .ToList();

                // Load History
                List<AudioTrackItem> historyItems = new();
                if (File.Exists(_historyFilePath))
                {
                    try
                    {
                        string json = File.ReadAllText(_historyFilePath);
                        var histPaths = JsonSerializer.Deserialize<List<string>>(json);
                        if (histPaths != null)
                        {
                            foreach (var hp in histPaths)
                            {
                                var match = items.FirstOrDefault(x => string.Equals(x.FilePath, hp, StringComparison.OrdinalIgnoreCase));
                                if (match != null) historyItems.Add(match);
                            }
                        }
                    }
                    catch { }
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    Playlist.Clear();
                    foreach (var it in items) Playlist.Add(it);

                    ArtistGroups.Clear();
                    foreach (var grp in artistGroups) ArtistGroups.Add(grp);

                    GenreGroups.Clear();
                    foreach (var gGrp in genreGroups) GenreGroups.Add(gGrp);

                    PlaybackHistory.Clear();
                    foreach (var hIt in historyItems) PlaybackHistory.Add(hIt);

                    _unshuffledContextQueue = Playlist.ToList();
                    ContextQueue = Playlist.ToList();

                    UserQueue.Clear();
                    foreach (string path in queuedPaths)
                    {
                        var queuedTrack = items.FirstOrDefault(item =>
                            string.Equals(
                                item.FilePath,
                                path,
                                StringComparison.OrdinalIgnoreCase));
                        if (queuedTrack != null)
                        {
                            UserQueue.Add(queuedTrack);
                        }
                    }

                    if (preserveActiveStream)
                    {
                        var activeTrack = items.FirstOrDefault(x =>
                            string.Equals(
                                x.FilePath,
                                activePath,
                                StringComparison.OrdinalIgnoreCase));
                        if (activeTrack != null)
                        {
                            CurrentTrack = activeTrack;
                            CurrentTrack.IsCurrentlyPlaying = true;
                            CurrentTrack.Duration = activeDuration;
                            _contextIndex = ContextQueue.IndexOf(CurrentTrack);
                            OnTrackChanged?.Invoke(CurrentTrack);
                            OnPositionChanged?.Invoke(
                                activePosition,
                                activeDuration);
                            OnPlaybackStateChanged?.Invoke(wasPlaying);
                        }
                        else if (activeTrackBeforeScan != null)
                        {
                            CurrentTrack = activeTrackBeforeScan;
                            CurrentTrack.IsCurrentlyPlaying = true;
                            _contextIndex = -1;
                            OnTrackChanged?.Invoke(CurrentTrack);
                            OnPositionChanged?.Invoke(
                                activePosition,
                                activeDuration);
                            OnPlaybackStateChanged?.Invoke(wasPlaying);
                        }
                    }
                    // Restore the saved session only on the first library scan.
                    else if (isInitialLibraryLoad && _savedSession != null)
                    {
                        _sessionRestored = true;
                        if (!string.IsNullOrWhiteSpace(_savedSession.LastTrackFilePath))
                        {
                            var lastTrack = items.FirstOrDefault(x => string.Equals(x.FilePath, _savedSession.LastTrackFilePath, StringComparison.OrdinalIgnoreCase));
                            if (lastTrack != null)
                            {
                                CurrentTrack = lastTrack;
                                CurrentTrack.IsCurrentlyPlaying = true;
                                _contextIndex = ContextQueue.IndexOf(lastTrack);

                                // Set pending seek BEFORE Open() — MediaOpened handler will apply it
                                if (_savedSession.PositionSeconds > 0)
                                {
                                    _pendingSeekPositionSeconds = _savedSession.PositionSeconds;
                                }

                                try
                                {
                                    _mediaPlayer.Open(new Uri(CurrentTrack.FilePath));
                                    // Do NOT set Position here — it has no effect before MediaOpened fires.
                                    // _pendingSeekPositionSeconds is applied in MediaPlayer_MediaOpened.
                                }
                                catch { }

                                OnTrackChanged?.Invoke(CurrentTrack);
                                // Fire an optimistic position update so the UI shows the saved position
                                if (_savedSession.PositionSeconds > 0)
                                {
                                    OnPositionChanged?.Invoke(TimeSpan.FromSeconds(_savedSession.PositionSeconds), CurrentTrack.Duration);
                                }
                            }
                        }
                    }
                    else if (!_sessionRestored)
                    {
                        // Mark as restored even if no session file existed
                        _sessionRestored = true;
                    }
                });
            });
        }

        public void PlayTrackItem(AudioTrackItem track, IEnumerable<AudioTrackItem>? currentContextList = null)
        {
            if (track == null) return;

            if (currentContextList != null)
            {
                _unshuffledContextQueue = currentContextList.ToList();
                if (IsShuffle)
                {
                    var shuffled = _unshuffledContextQueue.Where(x => x != track).OrderBy(_ => Random.Shared.Next()).ToList();
                    shuffled.Insert(0, track);
                    ContextQueue = shuffled;
                }
                else
                {
                    ContextQueue = _unshuffledContextQueue.ToList();
                }
            }

            int idx = ContextQueue.IndexOf(track);
            if (idx >= 0)
            {
                _contextIndex = idx;
            }

            StartPlayback(track);
        }

        private void StartPlayback(AudioTrackItem track)
        {
            if (track == null) return;

            if (CurrentTrack != null) CurrentTrack.IsCurrentlyPlaying = false;
            foreach (var item in Playlist) item.IsCurrentlyPlaying = false;

            CurrentTrack = track;
            CurrentTrack.IsCurrentlyPlaying = true;

            // Increment & Save Play Count
            track.PlayCount++;
            track.LastPlayedAt = DateTime.Now;
            _playCounts[track.FilePath] = track.PlayCount;
            SavePlayCounts();
            OnPlayCountsUpdated?.Invoke();

            // Update History
            PlaybackHistory.Remove(track);
            PlaybackHistory.Insert(0, track);
            while (PlaybackHistory.Count > 30) PlaybackHistory.RemoveAt(PlaybackHistory.Count - 1);
            SaveHistory();
            OnHistoryUpdated?.Invoke();

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
                OnQueueUpdated?.Invoke();
                SaveSession();
            }
            catch { }
        }

        public void Pause()
        {
            if (IsPlaying)
            {
                _mediaPlayer.Pause();
                IsPlaying = false;
                _positionTimer.Stop();
                OnPlaybackStateChanged?.Invoke(false);
                SaveSession();
            }
        }

        public void TogglePlayPause()
        {
            if (CurrentTrack == null)
            {
                if (UserQueue.Count > 0)
                {
                    var first = UserQueue[0];
                    UserQueue.RemoveAt(0);
                    StartPlayback(first);
                    return;
                }
                else if (ContextQueue.Count > 0)
                {
                    _contextIndex = 0;
                    StartPlayback(ContextQueue[0]);
                    return;
                }
                return;
            }

            if (IsPlaying)
            {
                _mediaPlayer.Pause();
                IsPlaying = false;
                _positionTimer.Stop();
                OnPlaybackStateChanged?.Invoke(false);
                SaveSession();
            }
            else
            {
                if (_mediaPlayer.Source == null && CurrentTrack != null)
                {
                    try
                    {
                        _mediaPlayer.Open(new Uri(CurrentTrack.FilePath));
                        if (_pendingSeekPositionSeconds > 0)
                        {
                            _mediaPlayer.Position = TimeSpan.FromSeconds(_pendingSeekPositionSeconds);
                            _pendingSeekPositionSeconds = -1;
                        }
                    }
                    catch { }
                }
                _mediaPlayer.Play();
                IsPlaying = true;
                _positionTimer.Start();
                OnPlaybackStateChanged?.Invoke(true);
                SaveSession();
            }
        }

        public void PlayNext()
        {
            if (RepeatMode == RepeatMode.RepeatOne && CurrentTrack != null)
            {
                StartPlayback(CurrentTrack);
                return;
            }

            // 1. Check User Manual Queue
            if (UserQueue.Count > 0)
            {
                var nextUserTrack = UserQueue[0];
                UserQueue.RemoveAt(0);
                OnQueueUpdated?.Invoke();
                StartPlayback(nextUserTrack);
                return;
            }

            // 2. Check Context Queue
            if (ContextQueue.Count > 0)
            {
                _contextIndex++;
                if (_contextIndex >= ContextQueue.Count)
                {
                    if (RepeatMode == RepeatMode.RepeatAll)
                    {
                        _contextIndex = 0;
                    }
                    else
                    {
                        IsPlaying = false;
                        _positionTimer.Stop();
                        OnPlaybackStateChanged?.Invoke(false);
                        return;
                    }
                }
                StartPlayback(ContextQueue[_contextIndex]);
            }
        }

        public void PlayPrevious()
        {
            if (ContextQueue.Count == 0) return;

            _contextIndex--;
            if (_contextIndex < 0)
            {
                _contextIndex = ContextQueue.Count - 1;
            }
            StartPlayback(ContextQueue[_contextIndex]);
        }

        public void Seek(double positionSeconds)
        {
            _mediaPlayer.Position = TimeSpan.FromSeconds(positionSeconds);
            SaveSession();
        }

        public void SetVolume(double volumePercent)
        {
            CurrentVolume = Math.Clamp(volumePercent / 100.0, 0.0, 1.0);
            _mediaPlayer.Volume = CurrentVolume;
            OnVolumeChanged?.Invoke(volumePercent);
            SaveSession();
        }

        private void MediaPlayer_MediaOpened(object? sender, EventArgs e)
        {
            if (_mediaPlayer.NaturalDuration.HasTimeSpan && CurrentTrack != null)
            {
                CurrentTrack.Duration = _mediaPlayer.NaturalDuration.TimeSpan;
            }
            if (_pendingSeekPositionSeconds > 0)
            {
                Seek(_pendingSeekPositionSeconds);
                _pendingSeekPositionSeconds = -1;
            }
        }

        private void MediaPlayer_MediaEnded(object? sender, EventArgs e)
        {
            PlayNext();
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
            byte[]? buffer = null;
            try
            {
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                int requestedLength = (int)Math.Min(fs.Length, 2 * 1024 * 1024);
                if (requestedLength <= 0) return null;
                buffer = ArrayPool<byte>.Shared.Rent(requestedLength);
                int read = fs.Read(buffer, 0, requestedLength);

                int imgHeader = -1;
                for (int i = 0; i < read - 4; i++)
                {
                    // Check JPEG header (0xFF 0xD8 0xFF)
                    if (buffer[i] == 0xFF && buffer[i + 1] == 0xD8 && buffer[i + 2] == 0xFF)
                    {
                        imgHeader = i;
                        break;
                    }
                    // Check PNG header (0x89 0x50 0x4E 0x47)
                    if (buffer[i] == 0x89 && buffer[i + 1] == 0x50 && buffer[i + 2] == 0x4E && buffer[i + 3] == 0x47)
                    {
                        imgHeader = i;
                        break;
                    }
                }

                if (imgHeader >= 0)
                {
                    using var ms = new MemoryStream(buffer, imgHeader, read - imgHeader);
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 100;
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch { }
            finally
            {
                if (buffer != null)
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

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
                if (fs.Read(header, 0, 10) == 10 && header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'2' || (header[0] == (byte)'I' && header[1] == (byte)'D' && header[2] == (byte)'3'))
                {
                    int tagSize = ((header[6] & 0x7F) << 21) | ((header[7] & 0x7F) << 14) | ((header[8] & 0x7F) << 7) | (header[9] & 0x7F);
                    byte[] tagBytes = new byte[tagSize];
                    int read = fs.Read(tagBytes, 0, tagSize);

                    string title = string.Empty;
                    List<string> artists = new();
                    string genre = string.Empty;

                    int idx = 0;
                    while (idx < read - 10)
                    {
                        string frameId = Encoding.ASCII.GetString(tagBytes, idx, 4);
                        if (!char.IsLetterOrDigit(frameId[0])) { idx++; continue; }

                        int frameSize = (tagBytes[idx + 4] << 24) | (tagBytes[idx + 5] << 16) | (tagBytes[idx + 6] << 8) | tagBytes[idx + 7];
                        if (frameSize <= 0 || frameSize > read - idx - 10) { idx++; continue; }

                        if (frameId == "TIT2" || frameId == "TPE1" || frameId == "TPE2" || frameId == "TCON")
                        {
                            string text = DecodeId3Text(tagBytes, idx + 10, frameSize);
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                if (frameId == "TIT2") title = text;
                                else if (frameId == "TPE1" || frameId == "TPE2") artists.Add(text);
                                else if (frameId == "TCON") genre = text;
                            }
                        }

                        idx += 10 + frameSize;
                    }

                    if (!string.IsNullOrWhiteSpace(title)) item.Title = title;
                    if (!string.IsNullOrWhiteSpace(genre)) item.Genre = NormalizeGenre(genre);

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

        private static string NormalizeGenre(string? genre)
        {
            if (string.IsNullOrWhiteSpace(genre)) return "General Music";

            // ID3v2 TCON may contain an ID3v1 numeric code such as "(13)Pop".
            // That number is a genre identifier, not a track count.
            string cleaned = Regex.Replace(
                genre.Trim(),
                @"^(?:\(\s*\d+\s*\)|\d+\s*[\)\].:-])\s*",
                string.Empty);
            return string.IsNullOrWhiteSpace(cleaned) ? "General Music" : cleaned.Trim();
        }

        private static string GetTrackIdentity(AudioTrackItem track)
        {
            static string NormalizePart(string value) =>
                Regex.Replace(
                    value.Trim().ToLowerInvariant(),
                    @"[^\p{L}\p{N}]+",
                    string.Empty);

            string title = NormalizePart(track.DisplayTitle);
            string artist = NormalizePart(track.DisplayArtist);
            return $"{artist}|{title}";
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
