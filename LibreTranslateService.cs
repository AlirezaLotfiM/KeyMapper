using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyMapper
{
    public sealed class TranslationResult
    {
        public string TranslatedText { get; init; } = string.Empty;
        public string DetectedLanguage { get; init; } = string.Empty;
    }

    public class LibreTranslateService
    {
        private static readonly Lazy<LibreTranslateService> _instance = new(() => new LibreTranslateService());
        public static LibreTranslateService Instance => _instance.Value;

        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        public async Task<TranslationResult> TranslateAsync(
            string text,
            string targetLanguage,
            string endpoint,
            string apiKey = "",
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Text cannot be empty.", nameof(text));

            string baseUrl = string.IsNullOrWhiteSpace(endpoint)
                ? "http://localhost:5000"
                : endpoint.Trim().TrimEnd('/');
            string translateUrl = baseUrl.EndsWith("/translate", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/translate";

            var payload = new
            {
                q = text,
                source = "auto",
                target = targetLanguage,
                format = "text",
                api_key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, translateUrl)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json")
            };
            using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
            string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                string message = $"LibreTranslate returned {(int)response.StatusCode}.";
                try
                {
                    using JsonDocument errorDocument = JsonDocument.Parse(responseBody);
                    if (errorDocument.RootElement.TryGetProperty("error", out JsonElement error))
                        message = error.GetString() ?? message;
                }
                catch { }
                throw new InvalidOperationException(message);
            }

            using JsonDocument document = JsonDocument.Parse(responseBody);
            if (!document.RootElement.TryGetProperty("translatedText", out JsonElement translated))
                throw new InvalidOperationException("LibreTranslate returned no translated text.");

            string detectedLanguage = string.Empty;
            if (document.RootElement.TryGetProperty("detectedLanguage", out JsonElement detected))
            {
                if (detected.ValueKind == JsonValueKind.Object &&
                    detected.TryGetProperty("language", out JsonElement language))
                {
                    detectedLanguage = language.GetString() ?? string.Empty;
                }
                else if (detected.ValueKind == JsonValueKind.String)
                {
                    detectedLanguage = detected.GetString() ?? string.Empty;
                }
            }

            return new TranslationResult
            {
                TranslatedText = translated.GetString() ?? string.Empty,
                DetectedLanguage = detectedLanguage
            };
        }
    }
}
