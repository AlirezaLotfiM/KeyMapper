using System;
using System.Runtime.InteropServices;

namespace KeyMapper
{
    public static class MediaControlService
    {
        private const byte VK_VOLUME_MUTE = 0xAD;
        private const byte VK_VOLUME_DOWN = 0xAE;
        private const byte VK_VOLUME_UP = 0xAF;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_STOP = 0xB2;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

        private const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public static void SendMediaKey(byte key)
        {
            keybd_event(key, 0, 0, UIntPtr.Zero);
            keybd_event(key, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static void PlayPause() => SendMediaKey(VK_MEDIA_PLAY_PAUSE);
        public static void NextTrack() => SendMediaKey(VK_MEDIA_NEXT_TRACK);
        public static void PreviousTrack() => SendMediaKey(VK_MEDIA_PREV_TRACK);
        public static void Stop() => SendMediaKey(VK_MEDIA_STOP);
        public static void VolumeUp(int steps = 5)
        {
            for (int i = 0; i < steps; i++)
            {
                SendMediaKey(VK_VOLUME_UP);
            }
        }
        public static void VolumeDown(int steps = 5)
        {
            for (int i = 0; i < steps; i++)
            {
                SendMediaKey(VK_VOLUME_DOWN);
            }
        }
        public static void ToggleMute() => SendMediaKey(VK_VOLUME_MUTE);
        public static void RestartTrack()
        {
            SendMediaKey(VK_MEDIA_PREV_TRACK);
            SendMediaKey(VK_MEDIA_PREV_TRACK);
        }
        public static void SetVolumePercent(int percent)
        {
            percent = Math.Clamp(percent, 0, 100);
            // Mute-zero baseline: send 50 VolumeDown keys to guarantee 0%
            for (int i = 0; i < 50; i++)
            {
                SendMediaKey(VK_VOLUME_DOWN);
            }
            // Send VolumeUp steps (each step is 2%)
            int steps = (int)Math.Round(percent / 2.0);
            for (int i = 0; i < steps; i++)
            {
                SendMediaKey(VK_VOLUME_UP);
            }
        }
    }
}
