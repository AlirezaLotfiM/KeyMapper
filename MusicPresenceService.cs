using System;
using System.Threading.Tasks;
using Windows.Media.Control;

namespace KeyMapper
{
    internal sealed record PlayingTrack(
        string Title,
        string Artist,
        string SourceAppId)
    {
        public string Key => $"{SourceAppId}|{Artist}|{Title}";
    }

    internal sealed class MusicPresenceService
    {
        public static MusicPresenceService Instance { get; } = new();

        private GlobalSystemMediaTransportControlsSessionManager? _manager;

        private MusicPresenceService()
        {
        }

        public async Task<PlayingTrack?> GetCurrentTrackAsync()
        {
            try
            {
                _manager ??=
                    await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                GlobalSystemMediaTransportControlsSession? session =
                    _manager.GetCurrentSession();
                if (session == null ||
                    session.GetPlaybackInfo().PlaybackStatus !=
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    return null;
                }

                GlobalSystemMediaTransportControlsSessionMediaProperties properties =
                    await session.TryGetMediaPropertiesAsync();
                string title = properties.Title?.Trim() ?? string.Empty;
                if (title.Length == 0) return null;

                string artist = properties.Artist?.Trim() ?? string.Empty;
                if (artist.Length == 0) artist = "the current artist";
                return new PlayingTrack(
                    title,
                    artist,
                    session.SourceAppUserModelId ?? string.Empty);
            }
            catch
            {
                // Some players do not publish a Windows media session. Music-aware
                // comments are optional and should never disturb the desktop pet.
                return null;
            }
        }
    }
}
