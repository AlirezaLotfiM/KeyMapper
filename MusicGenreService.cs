using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeyMapper
{
    public sealed record TrackDetails(
        string Title,
        string Artist,
        string Genre,
        string ReleaseYear);

    public class MusicGenreService
    {
        private static readonly Lazy<MusicGenreService> _instance =
            new(() => new MusicGenreService());
        public static MusicGenreService Instance => _instance.Value;

        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(5)
        };

        public async Task<TrackDetails> FetchTrackDetailsAsync(string title, string artist)
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return new TrackDetails(title, artist, "Unknown Genre", string.Empty);
            }

            try
            {
                string query = Uri.EscapeDataString($"{title} {artist}");
                string url = $"https://itunes.apple.com/search?term={query}&entity=song&limit=1";
                
                using HttpResponseMessage response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using JsonDocument doc = JsonDocument.Parse(json);
                    JsonElement root = doc.RootElement;
                    
                    if (root.TryGetProperty("resultCount", out JsonElement countElement) && countElement.GetInt32() > 0)
                    {
                        JsonElement item = root.GetProperty("results")[0];
                        string genre = item.TryGetProperty("primaryGenreName", out JsonElement g) ? g.GetString() ?? "Music" : "Music";
                        string releaseDate = item.TryGetProperty("releaseDate", out JsonElement r) ? r.GetString() ?? "" : "";
                        string year = releaseDate.Length >= 4 ? releaseDate[..4] : "";
                        
                        return new TrackDetails(title, artist, genre, year);
                    }
                }
            }
            catch
            {
                // Fallback gracefully if offline or request fails
            }

            return new TrackDetails(title, artist, "General Music", string.Empty);
        }
    }
}
