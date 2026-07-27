using System;
using System.Diagnostics;
using System.IO;
using NAudio.Wave;

namespace KeyMapper
{
    public class AudioRecorderService : IDisposable
    {
        private WaveInEvent? _waveIn;
        private WaveFileWriter? _writer;
        private AudioFileReader? _audioFileReader;
        private WaveOutEvent? _waveOut;

        public bool IsRecording { get; private set; }
        public bool IsPlaying { get; private set; }
        public string? CurrentRecordingPath { get; private set; }

        public event Action<double>? PlaybackProgressChanged;
        public event Action? PlaybackStopped;
        public event Action<double>? RecordingTimeUpdated;

        private System.Windows.Threading.DispatcherTimer? _recordingTimer;
        private System.Windows.Threading.DispatcherTimer? _playbackTimer;
        private DateTime _recordingStartTime;

        public string StartRecording(string destinationFolder)
        {
            StopRecording();
            StopPlayback();

            if (!Directory.Exists(destinationFolder))
            {
                Directory.CreateDirectory(destinationFolder);
            }

            string filename = $"voice_{Guid.NewGuid():N}.wav";
            CurrentRecordingPath = Path.Combine(destinationFolder, filename);

            try
            {
                _waveIn = new WaveInEvent
                {
                    WaveFormat = new WaveFormat(44100, 16, 1) // 44.1kHz, 16-bit, Mono
                };

                _writer = new WaveFileWriter(CurrentRecordingPath, _waveIn.WaveFormat);

                _waveIn.DataAvailable += (s, a) =>
                {
                    _writer?.Write(a.Buffer, 0, a.BytesRecorded);
                };

                _waveIn.RecordingStopped += (s, a) =>
                {
                    _writer?.Dispose();
                    _writer = null;
                    _waveIn?.Dispose();
                    _waveIn = null;
                };

                _waveIn.StartRecording();
                IsRecording = true;
                _recordingStartTime = DateTime.Now;

                _recordingTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                _recordingTimer.Tick += (s, e) =>
                {
                    double elapsed = (DateTime.Now - _recordingStartTime).TotalSeconds;
                    RecordingTimeUpdated?.Invoke(elapsed);
                };
                _recordingTimer.Start();

                return CurrentRecordingPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to start audio recording: {ex.Message}");
                IsRecording = false;
                return string.Empty;
            }
        }

        public double StopRecording()
        {
            if (!IsRecording) return 0;

            try
            {
                _recordingTimer?.Stop();
                _recordingTimer = null;

                double duration = (DateTime.Now - _recordingStartTime).TotalSeconds;
                _waveIn?.StopRecording();
                IsRecording = false;
                return Math.Round(duration, 1);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping recording: {ex.Message}");
                IsRecording = false;
                return 0;
            }
        }

        public void PlayAudio(string filePath)
        {
            StopPlayback();
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath)) return;

            try
            {
                _audioFileReader = new AudioFileReader(filePath);
                _waveOut = new WaveOutEvent();
                _waveOut.Init(_audioFileReader);

                _waveOut.PlaybackStopped += (s, a) =>
                {
                    IsPlaying = false;
                    _playbackTimer?.Stop();
                    PlaybackStopped?.Invoke();
                };

                _waveOut.Play();
                IsPlaying = true;

                _playbackTimer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(100)
                };
                _playbackTimer.Tick += (s, e) =>
                {
                    if (_audioFileReader != null && IsPlaying)
                    {
                        double current = _audioFileReader.CurrentTime.TotalSeconds;
                        PlaybackProgressChanged?.Invoke(current);
                    }
                };
                _playbackTimer.Start();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to play audio file: {ex.Message}");
                IsPlaying = false;
            }
        }

        public void StopPlayback()
        {
            if (_waveOut != null)
            {
                _playbackTimer?.Stop();
                _playbackTimer = null;
                _waveOut.Stop();
                _waveOut.Dispose();
                _waveOut = null;
            }

            if (_audioFileReader != null)
            {
                _audioFileReader.Dispose();
                _audioFileReader = null;
            }

            IsPlaying = false;
        }

        public void Dispose()
        {
            StopRecording();
            StopPlayback();
        }
    }
}
