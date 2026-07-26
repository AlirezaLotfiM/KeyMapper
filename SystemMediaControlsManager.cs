using System;
using System.Runtime.InteropServices;
using Windows.Media;

namespace KeyMapper
{
    public class SystemMediaControlsManager : IDisposable
    {
        [ComImport]
        [Guid("C671678B-54D4-4426-978C-271634B16C60")]
        [InterfaceType(ComInterfaceType.InterfaceIsIInspectable)]
        private interface ISystemMediaTransportControlsInterop
        {
            IntPtr GetForWindow(IntPtr appWindow, [In] ref Guid riid);
        }

        private static readonly Guid IID_ISystemMediaTransportControls = new Guid("99FA252E-9923-4D5C-9A6A-7A063D8446FD");

        private SystemMediaTransportControls? _smtc;

        public void Initialize(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;
            try
            {
                Guid interopGuid = new Guid("C671678B-54D4-4426-978C-271634B16C60");
                RoGetActivationFactory("Windows.Media.SystemMediaTransportControls", ref interopGuid, out object factory);
                var interop = (ISystemMediaTransportControlsInterop)factory;
                Guid iid = IID_ISystemMediaTransportControls;
                IntPtr smtcPtr = interop.GetForWindow(hwnd, ref iid);
                if (smtcPtr != IntPtr.Zero)
                {
                    _smtc = (SystemMediaTransportControls)Marshal.GetObjectForIUnknown(smtcPtr);
                    _smtc.IsPlayEnabled = true;
                    _smtc.IsPauseEnabled = true;
                    _smtc.IsNextEnabled = true;
                    _smtc.IsPreviousEnabled = true;
                    _smtc.IsStopEnabled = true;
                    _smtc.IsEnabled = true;
                    _smtc.ButtonPressed += Smtc_ButtonPressed;

                    LocalAudioPlayerService.Instance.OnTrackChanged += Instance_OnTrackChanged;
                    LocalAudioPlayerService.Instance.OnPlaybackStateChanged += Instance_OnPlaybackStateChanged;

                    if (LocalAudioPlayerService.Instance.CurrentTrack != null)
                    {
                        Instance_OnTrackChanged(LocalAudioPlayerService.Instance.CurrentTrack);
                        Instance_OnPlaybackStateChanged(LocalAudioPlayerService.Instance.IsPlaying);
                    }
                }
            }
            catch { }
        }

        [DllImport("combase.dll", EntryPoint = "RoGetActivationFactory", ExactSpelling = true, CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern int RoGetActivationFactory(
            [MarshalAs(UnmanagedType.HString)] string activatableClassId,
            [In] ref Guid iid,
            [Out, MarshalAs(UnmanagedType.IUnknown)] out object factory);

        private void Smtc_ButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
        {
            switch (args.Button)
            {
                case SystemMediaTransportControlsButton.Pause:
                case SystemMediaTransportControlsButton.Stop:
                    if (LocalAudioPlayerService.Instance.IsPlaying)
                    {
                        LocalAudioPlayerService.Instance.Pause();
                    }
                    break;
                case SystemMediaTransportControlsButton.Play:
                    if (!LocalAudioPlayerService.Instance.IsPlaying)
                    {
                        LocalAudioPlayerService.Instance.TogglePlayPause();
                    }
                    break;
                case SystemMediaTransportControlsButton.Next:
                    LocalAudioPlayerService.Instance.PlayNext();
                    break;
                case SystemMediaTransportControlsButton.Previous:
                    LocalAudioPlayerService.Instance.PlayPrevious();
                    break;
            }
        }

        private void Instance_OnPlaybackStateChanged(bool isPlaying)
        {
            if (_smtc != null)
            {
                try
                {
                    _smtc.PlaybackStatus = isPlaying ? MediaPlaybackStatus.Playing : MediaPlaybackStatus.Paused;
                }
                catch { }
            }
        }

        private void Instance_OnTrackChanged(AudioTrackItem? track)
        {
            if (_smtc != null && track != null)
            {
                try
                {
                    var updater = _smtc.DisplayUpdater;
                    updater.Type = MediaPlaybackType.Music;
                    updater.MusicProperties.Title = track.DisplayTitle;
                    updater.MusicProperties.Artist = track.DisplayArtist;
                    updater.MusicProperties.AlbumTitle = track.Album;
                    updater.Update();
                }
                catch { }
            }
        }

        public void Dispose()
        {
            if (_smtc != null)
            {
                try
                {
                    _smtc.ButtonPressed -= Smtc_ButtonPressed;
                    LocalAudioPlayerService.Instance.OnTrackChanged -= Instance_OnTrackChanged;
                    LocalAudioPlayerService.Instance.OnPlaybackStateChanged -= Instance_OnPlaybackStateChanged;
                }
                catch { }
            }
        }
    }
}
