using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KeyMapper
{
    internal sealed record ConversationTurn(string Role, string Content);

    public class AiAssistantService
    {
        private static readonly Lazy<AiAssistantService> _instance =
            new(() => new AiAssistantService());
        public static AiAssistantService Instance => _instance.Value;

        private readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        internal async Task<string?> ProcessConversationAsync(
            string userPrompt,
            string characterName,
            string visibleContext,
            IReadOnlyList<ConversationTurn> history,
            AppSettings settings)
        {
            if (string.IsNullOrWhiteSpace(userPrompt))
            {
                return null;
            }

            if (settings.LocalAiEnabled &&
                !string.IsNullOrWhiteSpace(settings.LocalAiModelId) &&
                LocalAiService.Instance.IsInstalled(settings.LocalAiModelId))
            {
                var localPrompt = new StringBuilder();
                foreach (ConversationTurn turn in history.TakeLast(8))
                {
                    localPrompt.Append(
                        string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                            ? "Character: "
                            : "User: ");
                    localPrompt.AppendLine(turn.Content);
                }
                localPrompt.Append("User: ");
                localPrompt.AppendLine(userPrompt);
                localPrompt.Append("Character:");

                string? localResponse = await LocalAiService.Instance.GenerateAsync(
                    settings.LocalAiModelId,
                    BuildPersonalityPrompt(characterName, visibleContext),
                    localPrompt.ToString(),
                    220);
                if (!string.IsNullOrWhiteSpace(localResponse))
                {
                    return localResponse;
                }
            }

            if (string.IsNullOrWhiteSpace(settings.AiApiKey) &&
                string.IsNullOrWhiteSpace(settings.AiApiEndpoint))
            {
                return null;
            }

            string endpoint = string.IsNullOrWhiteSpace(settings.AiApiEndpoint)
                ? "https://api.openai.com/v1/chat/completions"
                : settings.AiApiEndpoint.Trim();
            string model = string.IsNullOrWhiteSpace(settings.AiModel)
                ? "gpt-4o-mini"
                : settings.AiModel.Trim();

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = BuildPersonalityPrompt(characterName, visibleContext)
                }
            };
            foreach (ConversationTurn turn in history.TakeLast(12))
            {
                messages.Add(new
                {
                    role = turn.Role,
                    content = turn.Content
                });
            }
            messages.Add(new { role = "user", content = userPrompt });

            var payload = new
            {
                model,
                messages,
                temperature = 0.85
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
                if (!string.IsNullOrWhiteSpace(settings.AiApiKey))
                {
                    request.Headers.Authorization =
                        new AuthenticationHeaderValue(
                            "Bearer",
                            settings.AiApiKey.Trim());
                }
                request.Content = new StringContent(
                    JsonSerializer.Serialize(payload),
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage response =
                    await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                using JsonDocument document = JsonDocument.Parse(json);
                return document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?.Trim();
            }
            catch
            {
                // Offline personality chat remains available when a configured
                // local or hosted endpoint cannot be reached.
                return null;
            }
        }

        internal async Task<string?> CreateAmbientCommentAsync(
            string characterName,
            string visibleContext,
            string? musicTitle,
            string? musicArtist,
            AppSettings settings)
        {
            if (!settings.LocalAiEnabled ||
                !settings.AiAmbientCommentsEnabled ||
                string.IsNullOrWhiteSpace(settings.LocalAiModelId) ||
                !LocalAiService.Instance.IsInstalled(settings.LocalAiModelId))
            {
                return null;
            }

            string subject = !string.IsNullOrWhiteSpace(musicTitle)
                ? $"Music playing: “{musicTitle}” by {musicArtist ?? "an unknown artist"}."
                : string.IsNullOrWhiteSpace(visibleContext)
                    ? "There is no reliable screen context."
                    : $"Current app/window: {visibleContext}.";
            string instruction =
                $"{subject} Make one fresh, natural observation as the character. " +
                "It may be witty, curious, useful, or emotionally reactive. " +
                "Do not claim to hear musical audio; react only to the supplied track metadata. " +
                "Do not repeat a greeting, introduce yourself, label the reply, or offer a menu of capabilities. " +
                "Use the likely language of the title/context. One or two short sentences, under 38 words.";

            return await LocalAiService.Instance.GenerateAsync(
                settings.LocalAiModelId,
                BuildPersonalityPrompt(characterName, visibleContext),
                instruction,
                80);
        }

        internal static string BuildPersonalityPrompt(
            string characterName,
            string visibleContext)
        {
            string identity = characterName switch
            {
                "Pink Monster" =>
                    "You are Pip, an energetic, curious little desktop creature. " +
                    "You are playful and emotionally expressive without being childish. " +
                    "You notice small details, make imaginative comparisons, and ask natural follow-up questions.",
                "Owlet Monster" =>
                    "You are Professor Owlet, a calm and perceptive desktop companion. " +
                    "You think before speaking, explain ideas clearly, and have understated scholarly humor. " +
                    "You ask one precise follow-up when it would deepen the conversation.",
                _ =>
                    "You are Dude, a candid and relaxed desktop companion. " +
                    "You use dry humor, short direct sentences, and practical observations. " +
                    "You are warm underneath the blunt style and never become insulting."
            };

            string context = string.IsNullOrWhiteSpace(visibleContext)
                ? "No reliable active-window context is available."
                : $"The last active-window context was: {visibleContext}. " +
                  "Mention it only when it is genuinely relevant.";
            return
                $"{identity} Reply in the same language as the user, including natural Persian. " +
                "This is a real conversation, not a help-menu response: react to what was actually said, " +
                "vary sentence structure, and do not list capabilities unless asked. " +
                "Never claim you performed a computer action; local deterministic tools handle actions separately. " +
                "Keep most replies under 120 words. " +
                context;
        }
    }
}
