using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NAudio.Wave;

namespace KeyMapper
{
    internal enum MusicMood
    {
        Peaceful,
        Melancholic,
        Cheerful,
        Dramatic,
        Intense,
        Focused
    }

    internal sealed record MusicBeat(double Seconds, double Strength);

    internal sealed record MusicTrackAnalysis(
        double BeatsPerMinute,
        double Energy,
        MusicMood Mood,
        IReadOnlyList<MusicBeat> Beats,
        bool UsesAudioAnalysis);

    internal sealed class PetTrackMemory
    {
        public string FilePath { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public int TotalStarts { get; set; }
        public DateTime LastStartedAt { get; set; }
        public Dictionary<string, int> CharacterListens { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class MusicExperienceService
    {
        private const int MaximumAnalyzedMinutes = 12;
        private static readonly Lazy<MusicExperienceService> LazyInstance =
            new(() => new MusicExperienceService());
        private readonly ConcurrentDictionary<string, Task<MusicTrackAnalysis>>
            _analysisTasks = new(StringComparer.OrdinalIgnoreCase);
        private readonly SemaphoreSlim _analysisGate = new(1, 1);
        private readonly Dictionary<string, PetTrackMemory> _memories =
            new(StringComparer.OrdinalIgnoreCase);
        private readonly object _memoryLock = new();
        private readonly string _memoryFilePath;

        public static MusicExperienceService Instance => LazyInstance.Value;

        private MusicExperienceService()
        {
            string appDataDirectory = Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "KeyMapper");
            Directory.CreateDirectory(appDataDirectory);
            _memoryFilePath = Path.Combine(
                appDataDirectory,
                "pet_music_memories.json");
            LoadMemories();
        }

        public Task<MusicTrackAnalysis> GetAnalysisAsync(
            AudioTrackItem track)
        {
            if (string.IsNullOrWhiteSpace(track.FilePath) ||
                !File.Exists(track.FilePath))
            {
                return Task.FromResult(CreateMetadataAnalysis(track));
            }

            FileInfo file = new(track.FilePath);
            string cacheKey =
                $"{file.FullName}|{file.Length}|{file.LastWriteTimeUtc.Ticks}";
            TrimAnalysisCache();
            return _analysisTasks.GetOrAdd(
                cacheKey,
                _ => AnalyzeTrackQueuedAsync(track));
        }

        public MusicTrackAnalysis GetProvisionalAnalysis(
            AudioTrackItem track) =>
            CreateMetadataAnalysis(track);

        public PetTrackMemory RecordTrackStart(
            AudioTrackItem track,
            string characterName)
        {
            string key = GetTrackKey(track);
            lock (_memoryLock)
            {
                if (!_memories.TryGetValue(key, out PetTrackMemory? memory))
                {
                    memory = new PetTrackMemory
                    {
                        FilePath = track.FilePath,
                        Title = track.DisplayTitle,
                        Artist = track.DisplayArtist
                    };
                    _memories[key] = memory;
                }

                memory.Title = track.DisplayTitle;
                memory.Artist = track.DisplayArtist;
                memory.TotalStarts++;
                memory.LastStartedAt = DateTime.Now;
                memory.CharacterListens.TryGetValue(
                    characterName,
                    out int listens);
                memory.CharacterListens[characterName] = listens + 1;
                SaveMemories();
                return CloneMemory(memory);
            }
        }

        public int GetCharacterListenCount(
            AudioTrackItem track,
            string characterName)
        {
            lock (_memoryLock)
            {
                return _memories.TryGetValue(
                           GetTrackKey(track),
                           out PetTrackMemory? memory) &&
                       memory.CharacterListens.TryGetValue(
                           characterName,
                           out int listens)
                    ? listens
                    : 0;
            }
        }

        public int GetCharacterAffinity(
            string characterName,
            string genre,
            MusicMood mood)
        {
            string normalizedGenre = genre.ToLowerInvariant();
            string[] preferred = characterName switch
            {
                "Pink Monster" =>
                    ["pop", "dance", "indie", "electronic"],
                "Owlet Monster" =>
                    ["classical", "ambient", "instrumental", "jazz"],
                "Dude Monster" =>
                    ["rock", "metal", "electro", "hip hop"],
                "Frieren" =>
                    ["classical", "orchestral", "folk", "ambient"],
                "Yuji Itadori" =>
                    ["rock", "hip hop", "dance", "electro"],
                "Monkey D. Luffy" =>
                    ["dance", "folk", "rock", "pop"],
                _ => []
            };
            string[] lessPreferred = characterName switch
            {
                "Owlet Monster" => ["metal", "hardcore"],
                "Frieren" => ["hardcore", "industrial"],
                "Dude Monster" => ["sleep", "meditation"],
                _ => []
            };

            if (preferred.Any(normalizedGenre.Contains))
                return 1;
            if (lessPreferred.Any(normalizedGenre.Contains))
                return -1;

            return characterName switch
            {
                "Pink Monster" when mood == MusicMood.Cheerful => 1,
                "Owlet Monster" when mood is MusicMood.Peaceful
                    or MusicMood.Focused => 1,
                "Dude Monster" when mood == MusicMood.Intense => 1,
                "Frieren" when mood is MusicMood.Peaceful
                    or MusicMood.Melancholic => 1,
                "Yuji Itadori" when mood is MusicMood.Intense
                    or MusicMood.Cheerful => 1,
                "Monkey D. Luffy" when mood == MusicMood.Cheerful => 1,
                _ => 0
            };
        }

        private static MusicTrackAnalysis AnalyzeTrack(AudioTrackItem track)
        {
            try
            {
                using var reader = new AudioFileReader(track.FilePath);
                int channels = Math.Max(1, reader.WaveFormat.Channels);
                int sampleRate = Math.Max(8000, reader.WaveFormat.SampleRate);
                int blockFrames = Math.Max(256, sampleRate / 20);
                float[] buffer = new float[blockFrames * channels];
                var rollingEnergy = new Queue<double>(30);
                var beats = new List<MusicBeat>();
                double lowPass = 0;
                double dcEstimate = 0;
                double alpha = 1 - Math.Exp(
                    -2 * Math.PI * 180 / sampleRate);
                double totalEnergy = 0;
                long analyzedFrames = 0;
                double lastBeatSeconds = -1;
                long maximumFrames =
                    (long)sampleRate * 60 * MaximumAnalyzedMinutes;

                while (analyzedFrames < maximumFrames)
                {
                    int read = reader.Read(buffer, 0, buffer.Length);
                    if (read <= 0) break;

                    int framesRead = read / channels;
                    double blockEnergy = 0;
                    for (int frame = 0; frame < framesRead; frame++)
                    {
                        double mono = 0;
                        int offset = frame * channels;
                        for (int channel = 0; channel < channels; channel++)
                        {
                            mono += buffer[offset + channel];
                        }
                        mono /= channels;

                        lowPass += alpha * (mono - lowPass);
                        dcEstimate += 0.004 * (lowPass - dcEstimate);
                        double bass = lowPass - dcEstimate;
                        blockEnergy += bass * bass;
                    }

                    blockEnergy /= Math.Max(1, framesRead);
                    double rollingAverage = rollingEnergy.Count > 0
                        ? rollingEnergy.Average()
                        : blockEnergy;
                    double strength = blockEnergy /
                                      Math.Max(0.0000001, rollingAverage);
                    double blockTime =
                        analyzedFrames / (double)sampleRate;
                    if (rollingEnergy.Count >= 14 &&
                        strength >= 1.48 &&
                        blockEnergy >= 0.000012 &&
                        blockTime - lastBeatSeconds >= 0.24)
                    {
                        beats.Add(new MusicBeat(
                            blockTime,
                            Math.Clamp(
                                (strength - 1.25) / 2.25,
                                0.25,
                                1)));
                        lastBeatSeconds = blockTime;
                    }

                    rollingEnergy.Enqueue(blockEnergy);
                    if (rollingEnergy.Count > 30)
                        rollingEnergy.Dequeue();
                    totalEnergy += blockEnergy * framesRead;
                    analyzedFrames += framesRead;
                }

                double durationSeconds =
                    analyzedFrames / (double)sampleRate;
                double bpm = EstimateTempo(beats);
                double rms = Math.Sqrt(
                    totalEnergy / Math.Max(1, analyzedFrames));
                double normalizedEnergy = Math.Clamp(
                    (rms - 0.008) / 0.105,
                    0,
                    1);
                MusicMood mood = DetectMood(
                    track,
                    bpm,
                    normalizedEnergy);

                if (beats.Count < 4)
                {
                    beats = CreateFallbackBeats(
                        bpm,
                        durationSeconds,
                        normalizedEnergy);
                }

                return new MusicTrackAnalysis(
                    bpm,
                    normalizedEnergy,
                    mood,
                    beats,
                    true);
            }
            catch
            {
                return CreateMetadataAnalysis(track);
            }
        }

        private async Task<MusicTrackAnalysis> AnalyzeTrackQueuedAsync(
            AudioTrackItem track)
        {
            await _analysisGate.WaitAsync();
            try
            {
                return await Task.Run(() => AnalyzeTrack(track));
            }
            finally
            {
                _analysisGate.Release();
            }
        }

        private void TrimAnalysisCache()
        {
            if (_analysisTasks.Count < 12) return;
            foreach (KeyValuePair<string, Task<MusicTrackAnalysis>> entry in
                     _analysisTasks.Where(pair => pair.Value.IsCompleted)
                         .Take(Math.Max(1, _analysisTasks.Count - 10)))
            {
                _analysisTasks.TryRemove(entry.Key, out _);
            }
        }

        private static MusicTrackAnalysis CreateMetadataAnalysis(
            AudioTrackItem track)
        {
            int seed = StringComparer.OrdinalIgnoreCase.GetHashCode(
                $"{track.DisplayTitle}|{track.DisplayArtist}");
            double bpm = 82 + Math.Abs(seed % 66);
            double energy = 0.35 + (Math.Abs(seed / 67 % 50) / 100.0);
            MusicMood mood = DetectMood(track, bpm, energy);
            double duration = track.Duration.TotalSeconds > 0
                ? track.Duration.TotalSeconds
                : 360;
            return new MusicTrackAnalysis(
                bpm,
                energy,
                mood,
                CreateFallbackBeats(bpm, duration, energy),
                false);
        }

        private static List<MusicBeat> CreateFallbackBeats(
            double bpm,
            double durationSeconds,
            double energy)
        {
            double interval = 60 / Math.Clamp(bpm, 60, 180);
            var beats = new List<MusicBeat>(
                (int)Math.Min(3000, durationSeconds / interval));
            int beatNumber = 0;
            for (double seconds = 0;
                 seconds <= durationSeconds && beats.Count < 3000;
                 seconds += interval)
            {
                double strength = beatNumber % 4 == 0
                    ? Math.Clamp(0.7 + energy * 0.3, 0, 1)
                    : Math.Clamp(0.35 + energy * 0.35, 0, 1);
                beats.Add(new MusicBeat(seconds, strength));
                beatNumber++;
            }
            return beats;
        }

        private static double EstimateTempo(IReadOnlyList<MusicBeat> beats)
        {
            if (beats.Count < 4) return 104;
            double[] intervals = beats
                .Zip(beats.Skip(1), (first, second) =>
                    second.Seconds - first.Seconds)
                .Where(interval => interval is >= 0.25 and <= 1.25)
                .OrderBy(interval => interval)
                .ToArray();
            if (intervals.Length == 0) return 104;

            double median = intervals[intervals.Length / 2];
            double bpm = 60 / median;
            while (bpm < 70) bpm *= 2;
            while (bpm > 180) bpm /= 2;
            return Math.Round(bpm);
        }

        private static MusicMood DetectMood(
            AudioTrackItem track,
            double bpm,
            double energy)
        {
            string text =
                $"{track.Genre} {track.Title} {track.Album}".ToLowerInvariant();
            if (ContainsAny(
                    text,
                    "sad",
                    "melanch",
                    "blues",
                    "grief",
                    "lonely",
                    "غم",
                    "تنهایی"))
                return MusicMood.Melancholic;
            if (ContainsAny(
                    text,
                    "soundtrack",
                    "score",
                    "orchestra",
                    "cinematic",
                    "epic"))
                return MusicMood.Dramatic;
            if (ContainsAny(
                    text,
                    "ambient",
                    "classical",
                    "meditation",
                    "sleep",
                    "acoustic") &&
                energy < 0.62)
                return MusicMood.Peaceful;
            if (ContainsAny(
                    text,
                    "metal",
                    "hardcore",
                    "edm",
                    "electro",
                    "drum and bass") ||
                energy > 0.72 ||
                bpm > 148)
                return MusicMood.Intense;
            if (ContainsAny(
                    text,
                    "pop",
                    "dance",
                    "funk",
                    "disco",
                    "happy") ||
                (bpm > 112 && energy > 0.42))
                return MusicMood.Cheerful;
            return MusicMood.Focused;
        }

        private static bool ContainsAny(
            string text,
            params string[] values) =>
            values.Any(text.Contains);

        private static string GetTrackKey(AudioTrackItem track) =>
            string.IsNullOrWhiteSpace(track.FilePath)
                ? $"{track.DisplayTitle}|{track.DisplayArtist}"
                : Path.GetFullPath(track.FilePath);

        private void LoadMemories()
        {
            try
            {
                if (!File.Exists(_memoryFilePath)) return;
                string json = File.ReadAllText(_memoryFilePath);
                List<PetTrackMemory>? memories =
                    JsonSerializer.Deserialize<List<PetTrackMemory>>(json);
                if (memories == null) return;
                foreach (PetTrackMemory memory in memories)
                {
                    string key = string.IsNullOrWhiteSpace(memory.FilePath)
                        ? $"{memory.Title}|{memory.Artist}"
                        : Path.GetFullPath(memory.FilePath);
                    memory.CharacterListens =
                        new Dictionary<string, int>(
                            memory.CharacterListens,
                            StringComparer.OrdinalIgnoreCase);
                    _memories[key] = memory;
                }
            }
            catch
            {
                _memories.Clear();
            }
        }

        private void SaveMemories()
        {
            try
            {
                string json = JsonSerializer.Serialize(
                    _memories.Values,
                    new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_memoryFilePath, json);
            }
            catch
            {
                // Music memory is optional and must never interrupt playback.
            }
        }

        private static PetTrackMemory CloneMemory(
            PetTrackMemory memory) =>
            new()
            {
                FilePath = memory.FilePath,
                Title = memory.Title,
                Artist = memory.Artist,
                TotalStarts = memory.TotalStarts,
                LastStartedAt = memory.LastStartedAt,
                CharacterListens = new Dictionary<string, int>(
                    memory.CharacterListens,
                    StringComparer.OrdinalIgnoreCase)
            };
    }
}
