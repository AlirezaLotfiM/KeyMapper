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
    }
}
