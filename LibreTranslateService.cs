using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        private static readonly IReadOnlyDictionary<string, string> TechnicalTerms =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["HTTPS"] = "Https",
                ["HTTP"] = "Http",
                ["JPEG"] = "Jpeg",
                ["JSON"] = "Json",
                ["HTML"] = "Html",
                ["SVG"] = "Svg",
                ["PNG"] = "Png",
                ["JPG"] = "Jpg",
                ["CSS"] = "Css",
                ["API"] = "Api",
                ["URL"] = "Url",
                ["SQL"] = "Sql",
                ["CPU"] = "Cpu",
                ["GPU"] = "Gpu",
                ["RAM"] = "Ram",
                ["OCR"] = "Ocr",
                ["PDF"] = "Pdf",
                ["UI"] = "Ui",
                ["UX"] = "Ux"
            };

        private static readonly IReadOnlyDictionary<string, string> PersianFormalWords =
            new Dictionary<string, string>
            {
                ["نیستن"] = "نیستند",
                ["هستن"] = "هستند",
                ["باشن"] = "باشند",
                ["دارن"] = "دارند",
                ["نمیشه"] = "نمی‌شود",
                ["میشه"] = "می‌شود"
            };

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

            PreparedSource prepared = PrepareSource(text);
            var payload = new
            {
                q = prepared.Text,
                source = prepared.SourceLanguage,
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

            if (detectedLanguage.Length == 0 && prepared.SourceLanguage != "auto")
                detectedLanguage = prepared.SourceLanguage;

            return new TranslationResult
            {
                TranslatedText = RestoreTechnicalTerms(
                    translated.GetString() ?? string.Empty,
                    prepared.TechnicalTerms),
                DetectedLanguage = detectedLanguage
            };
        }

        internal static string NormalizePersianForTranslation(string text)
        {
            string normalized = text
                .Replace('\u064A', '\u06CC') // Arabic yeh -> Persian yeh
                .Replace('\u0643', '\u06A9') // Arabic kaf -> Persian kaf
                .Replace('\u200F', ' ')
                .Replace('\u200E', ' ');

            normalized = Regex.Replace(normalized, @"[ \t]+", " ");
            normalized = Regex.Replace(
                normalized,
                @"(?<word>[\u0600-\u06FF]+)\s+ها(?![\u0600-\u06FF])",
                "${word}\u200Cها");

            foreach ((string colloquial, string formal) in PersianFormalWords)
            {
                normalized = Regex.Replace(
                    normalized,
                    $@"(?<![\u0600-\u06FF]){Regex.Escape(colloquial)}(?![\u0600-\u06FF])",
                    formal);
            }

            foreach ((string canonical, string modelFriendly) in TechnicalTerms)
            {
                normalized = Regex.Replace(
                    normalized,
                    $@"(?<![A-Za-z0-9]){Regex.Escape(canonical)}(?![A-Za-z0-9])",
                    modelFriendly,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return normalized.Trim();
        }

        private static string RestoreTechnicalTerms(
            string text,
            IReadOnlyList<string> termsToRestore)
        {
            string restored = text;
            foreach (string canonical in termsToRestore)
            {
                string modelFriendly = TechnicalTerms[canonical];
                restored = Regex.Replace(
                    restored,
                    $@"(?<![A-Za-z0-9]){Regex.Escape(modelFriendly)}(?![A-Za-z0-9])",
                    canonical,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            }

            return restored;
        }

        private static PreparedSource PrepareSource(string text)
        {
            bool isPersian = Regex.IsMatch(text, @"[\u0600-\u06FF]");
            if (!isPersian)
                return new PreparedSource(text.Trim(), "auto", Array.Empty<string>());

            var termsToRestore = new List<string>();
            foreach (string canonical in TechnicalTerms.Keys)
            {
                if (Regex.IsMatch(
                    text,
                    $@"(?<![A-Za-z0-9]){Regex.Escape(canonical)}(?![A-Za-z0-9])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                {
                    termsToRestore.Add(canonical);
                }
            }

            return new PreparedSource(
                NormalizePersianForTranslation(text),
                "fa",
                termsToRestore);
        }

        private sealed record PreparedSource(
            string Text,
            string SourceLanguage,
            IReadOnlyList<string> TechnicalTerms);
    }
}
