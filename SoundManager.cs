using System;
using System.IO;
using System.Media;

namespace KeyMapper
{
    public static class SoundManager
    {
        public static bool PlaySounds { get; set; } = true;

        private static SoundPlayer? _tickPlayer;
        private static SoundPlayer? _successPlayer;
        private static SoundPlayer? _cancelPlayer;

        private static SoundPlayer? _alarmPlayer;
        private static SoundPlayer? _chimePlayer;

        static SoundManager()
        {
            InitializePlayers();
        }

        private static void InitializePlayers()
        {
            try
            {
                // Standard clicky sound for starting recording
                string tickPath = @"C:\Windows\Media\Windows Navigation Start.wav";
                if (File.Exists(tickPath))
                {
                    _tickPlayer = new SoundPlayer(tickPath);
                    _tickPlayer.Load();
                }

                // Sleek notification sound for successful replacements
                string successPath = @"C:\Windows\Media\notify.wav";
                if (File.Exists(successPath))
                {
                    _successPlayer = new SoundPlayer(successPath);
                    _successPlayer.Load();
                }

                // Low cancel/pause sound
                string cancelPath = @"C:\Windows\Media\Windows Background.wav";
                if (File.Exists(cancelPath))
                {
                    _cancelPlayer = new SoundPlayer(cancelPath);
                    _cancelPlayer.Load();
                }

                // Alarm Sound
                string alarmPath = @"C:\Windows\Media\alarm01.wav";
                if (!File.Exists(alarmPath)) alarmPath = @"C:\Windows\Media\Windows Notify Calendar.wav";
                if (!File.Exists(alarmPath)) alarmPath = @"C:\Windows\Media\tada.wav";
                if (File.Exists(alarmPath))
                {
                    _alarmPlayer = new SoundPlayer(alarmPath);
                    _alarmPlayer.Load();
                }

                // Chime Sound
                string chimePath = @"C:\Windows\Media\chimes.wav";
                if (!File.Exists(chimePath)) chimePath = @"C:\Windows\Media\ding.wav";
                if (File.Exists(chimePath))
                {
                    _chimePlayer = new SoundPlayer(chimePath);
                    _chimePlayer.Load();
                }
            }
            catch
            {
                // Fallback / ignore loading errors
            }
        }

        public static void PlayTick()
        {
            if (!PlaySounds) return;
            try
            {
                _tickPlayer?.Play();
            }
            catch
            {
                // Ignore
            }
        }

        public static void PlaySuccess()
        {
            if (!PlaySounds) return;
            try
            {
                _successPlayer?.Play();
            }
            catch
            {
                // Ignore
            }
        }

        public static void PlayCancel()
        {
            if (!PlaySounds) return;
            try
            {
                _cancelPlayer?.Play();
            }
            catch
            {
                // Ignore
            }
        }

        public static void PlayAlarmSound()
        {
            if (!PlaySounds) return;
            try
            {
                if (_alarmPlayer != null) _alarmPlayer.Play();
                else SystemSounds.Exclamation.Play();
            }
            catch { try { SystemSounds.Exclamation.Play(); } catch { } }
        }

        public static void PlayHourlyChime()
        {
            if (!PlaySounds) return;
            try
            {
                if (_chimePlayer != null) _chimePlayer.Play();
                else SystemSounds.Asterisk.Play();
            }
            catch { try { SystemSounds.Asterisk.Play(); } catch { } }
        }
    }
}
