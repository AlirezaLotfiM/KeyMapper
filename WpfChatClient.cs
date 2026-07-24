using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace KeyMapper
{
    /// <summary>
    /// HTTP client for Cloudflare Worker chat agent endpoints.
    /// Supports both standard chat endpoint and streaming requests.
    /// </summary>
    public sealed class WpfChatClient : IDisposable
    {
        private static readonly HttpClient _http = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        private readonly string _baseUrl;
        private readonly string _token;

        public WpfChatClient(string baseUrl, string token)
        {
            _baseUrl = (baseUrl ?? string.Empty).TrimEnd('/');
            _token = token ?? string.Empty;
        }

        public async Task<bool> VerifyConnectionAsync()
        {
            try
            {
                using var req = BuildRequest(
                    HttpMethod.Get,
                    $"/api/wpf/verify?token={Uri.EscapeDataString(_token)}");

                using HttpResponseMessage res = await _http.SendAsync(req);
                return res.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<string?> SendMessageAsync(
            string sessionId,
            string message,
            string systemPrompt,
            string tone = "friendly")
        {
            var body = new
            {
                sessionId,
                message,
                model = "workersai",
                systemPrompt,
                tone
            };

            try
            {
                using var req = BuildRequest(HttpMethod.Post, "/api/wpf/chat");
                req.Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage res = await _http.SendAsync(req);
                if (!res.IsSuccessStatusCode) return null;

                string raw = await res.Content.ReadAsStringAsync();
                
                // Parse text chunks or JSON response
                var sb = new StringBuilder();
                using var reader = new StringReader(raw);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith("data: ", StringComparison.Ordinal)) continue;
                    string data = line["data: ".Length..].Trim();
                    if (data == "[DONE]") break;

                    try
                    {
                        using var doc = JsonDocument.Parse(data);
                        if (doc.RootElement.TryGetProperty("0", out var tokenEl))
                        {
                            sb.Append(tokenEl.GetString());
                        }
                    }
                    catch (JsonException)
                    {
                        sb.Append(data);
                    }
                }

                string result = sb.ToString().Trim();
                return result.Length > 0 ? result : null;
            }
            catch
            {
                return null;
            }
        }

        private HttpRequestMessage BuildRequest(HttpMethod method, string path)
        {
            var req = new HttpRequestMessage(method, _baseUrl + path);
            if (!string.IsNullOrWhiteSpace(_token))
            {
                req.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);
            }
            return req;
        }

        public void Dispose() { }
    }
}
