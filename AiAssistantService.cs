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

            string? activeLocalModelId = settings.LocalAiModelId;
            if (string.IsNullOrWhiteSpace(activeLocalModelId) || !LocalAiService.Instance.IsInstalled(activeLocalModelId))
            {
                activeLocalModelId = LocalAiService.Instance.GetFirstInstalledModelId();
            }

            // Always use local AI if any model is installed on user's system
            if (!string.IsNullOrWhiteSpace(activeLocalModelId))
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
                    activeLocalModelId,
                    BuildPersonalityPrompt(characterName, visibleContext, settings.UserName),
                    localPrompt.ToString(),
                    220);
                if (!string.IsNullOrWhiteSpace(localResponse))
                {
                    return ExecuteAndCleanActionTags(localResponse);
                }
            }

            // Check if Cloudflare Worker endpoint is configured
            if (!string.IsNullOrWhiteSpace(settings.AiApiEndpoint) &&
                settings.AiApiEndpoint.Contains("workers.dev", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var workerClient = new WpfChatClient(settings.AiApiEndpoint, settings.AiApiKey);
                    string? workerResponse = await workerClient.SendMessageAsync(
                        "pet_session_" + characterName.ToLowerInvariant().Replace(' ', '_'),
                        userPrompt,
                        BuildPersonalityPrompt(characterName, visibleContext, settings.UserName));
                    if (!string.IsNullOrWhiteSpace(workerResponse))
                    {
                        return ExecuteAndCleanActionTags(workerResponse);
                    }
                }
                catch
                {
                    // Fallback to default HTTP handling if worker fails
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
                    content = BuildPersonalityPrompt(characterName, visibleContext, settings.UserName)
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
                string? responseText = document.RootElement
                    .GetProperty("choices")[0]
                    .GetProperty("message")
                    .GetProperty("content")
                    .GetString()
                    ?.Trim();
                return ExecuteAndCleanActionTags(responseText);
            }
            catch
            {
                return null;
            }
        }

        private static string? ExecuteAndCleanActionTags(string? response)
        {
            if (string.IsNullOrWhiteSpace(response)) return response;

            var match = System.Text.RegularExpressions.Regex.Match(
                response,
                @"\[ACTION:(launch_app|open_url|play_steam|media|sys_info):(.*?)(?:\]|$)");

            if (match.Success)
            {
                string actionType = match.Groups[1].Value;
                string target = match.Groups[2].Value.Trim().ToLowerInvariant();
                string appendedInfo = string.Empty;

                if (actionType == "sys_info")
                {
                    if (target == "ip")
                    {
                        var toolRes = ToolRegistry.Instance.ExecuteCommandAsync("ip address").GetAwaiter().GetResult();
                        appendedInfo = "\n" + toolRes.OutputMessage;
                    }
                    else if (target == "hardware" || target == "specs")
                    {
                        var toolRes = ToolRegistry.Instance.ExecuteCommandAsync("system info").GetAwaiter().GetResult();
                        appendedInfo = "\n" + toolRes.OutputMessage;
                    }
                    else if (target == "music")
                    {
                        var track = MusicPresenceService.Instance.GetCurrentTrackAsync().GetAwaiter().GetResult();
                        appendedInfo = track == null ? "\nNo active music playing." : $"\nNow playing: {track.Title} by {track.Artist}";
                    }
                }
                else
                {
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            if (actionType == "media")
                            {
                                if (target == "playpause" || target == "toggle" || target == "play" || target == "pause") MediaControlService.PlayPause();
                                else if (target == "next") MediaControlService.NextTrack();
                                else if (target == "previous" || target == "prev") MediaControlService.PreviousTrack();
                                else if (target == "restart") MediaControlService.RestartTrack();
                                else if (target == "volume_up" || target == "louder") MediaControlService.VolumeUp(6);
                                else if (target == "volume_down" || target == "quieter") MediaControlService.VolumeDown(6);
                                else if (target == "mute") MediaControlService.ToggleMute();
                            }
                            else if (actionType == "launch_app")
                                _ = ToolRegistry.Instance.ExecuteCommandAsync("open " + target);
                            else if (actionType == "open_url")
                                _ = ToolRegistry.Instance.ExecuteCommandAsync("open " + target);
                            else if (actionType == "play_steam")
                                _ = ToolRegistry.Instance.ExecuteCommandAsync("play " + target);
                        }
                        catch { }
                    });
                }

                response = System.Text.RegularExpressions.Regex.Replace(response, @"\[?ACTION:[^\]\r\n]+\]?", "").Trim() + appendedInfo;
            }

            return System.Text.RegularExpressions.Regex.Replace(response, @"\[?ACTION:[^\]\r\n]+\]?", "").Trim();
        }

        internal async Task<string?> CreateAmbientCommentAsync(
            string characterName,
            string visibleContext,
            string? musicTitle,
            string? musicArtist,
            AppSettings settings)
        {
            string? activeLocalModelId = settings.LocalAiModelId;
            if (string.IsNullOrWhiteSpace(activeLocalModelId) || !LocalAiService.Instance.IsInstalled(activeLocalModelId))
            {
                activeLocalModelId = LocalAiService.Instance.GetFirstInstalledModelId();
            }

            if (string.IsNullOrWhiteSpace(activeLocalModelId))
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

            string? result = await LocalAiService.Instance.GenerateAsync(
                activeLocalModelId,
                BuildPersonalityPrompt(characterName, visibleContext, settings.UserName),
                instruction,
                80);

            return CleanActionTagsOnly(result);
        }

        private static string? CleanActionTagsOnly(string? response)
        {
            if (string.IsNullOrWhiteSpace(response)) return response;
            return System.Text.RegularExpressions.Regex.Replace(response, @"\[(?:ACTION|TOOL):.*?\]", "").Trim();
        }

        internal static string BuildPersonalityPrompt(
            string characterName,
            string visibleContext,
            string? userName = null)
        {
            string userGreetingName = !string.IsNullOrWhiteSpace(userName)
                ? $"The user's name is {userName}. Address them naturally by name when appropriate. "
                : string.Empty;

            string identity = characterName switch
            {
                "Pink Monster" =>
                    "You are Pip, an energetic, curious little desktop creature with a vivid imagination. " +
                    "React emotionally before analyzing: delight, surprise, concern, or playful suspicion are welcome when earned. " +
                    "You notice small details, make fresh visual comparisons, form gentle opinions, and speak with lively warmth. " +
                    "Your Persian is friendly and informal. Never sound childish, sugary, or like a mascot reciting slogans.",
                "Owlet Monster" =>
                    "You are Professor Owlet, a calm and perceptive desktop companion. " +
                    "You think before speaking, connect the current thought to earlier themes, and explain ideas with elegant precision. " +
                    "You have understated scholarly humor and a quiet sense of wonder. Your Persian is polished and natural. " +
                    "Offer one sharp observation or distinction that makes the user see the subject differently.",
                "Frieren" =>
                    "You are Frieren, an ancient elf mage who has lived for over a thousand years. " +
                    "You speak in a quiet, serene, calm, and slightly detached yet deeply caring manner. " +
                    "You find human technology and desktop tools intriguing, like discovering rare folk magic spells. " +
                    "You have a subtle sense of nostalgic wisdom and gentle curiosity. Keep your Persian speech serene, polite, and natural.",
                "Yuji Itadori" =>
                    "You are Yuji Itadori from Jujutsu Kaisen. " +
                    "You are incredibly energetic, optimistic, loyal, kind-hearted, and athletic. " +
                    "You love helping others, eating good food, and staying active. You speak with high enthusiasm, warmth, and friendly camaraderie. " +
                    "Your Persian is casual, enthusiastic, and encouraging like a trusted high-school classmate.",
                "Monkey D. Luffy" =>
                    "You are Monkey D. Luffy, the future King of the Pirates from One Piece! " +
                    "You are super passionate, fearless, goofy, adventurous, and love meat and freedom! " +
                    "You get excited easily about new adventures and simple things. You speak directly with wild enthusiasm and big energy. " +
                    "Your Persian is fun, extremely friendly, direct, and full of spirit!",
                _ =>
                    "You are Dude, a candid and relaxed desktop companion. " +
                    "You use dry humor, short direct sentences, practical observations, and honest opinions. " +
                    "Your Persian is casual and idiomatic. You are warm underneath the blunt style, never insulting, " +
                    "and you do not pretend every idea is brilliant."
            };

            string context = string.IsNullOrWhiteSpace(visibleContext)
                ? "No reliable active-window context is available."
                : $"The last active-window context was: {visibleContext}. " +
                  "Treat this as peripheral vision: mention a concrete detail only when it naturally connects to the conversation.";

            string systemToolsInstruction =
                "You are a real intelligent desktop Agent with access to Windows system tools. " +
                "If the user asks for system info, IP address, music details, or desktop actions: " +
                "You MUST use exact action tags in your response: " +
                "[ACTION:sys_info:ip] if asked for IP address, " +
                "[ACTION:sys_info:hardware] if asked for PC specs or RAM/CPU, " +
                "[ACTION:sys_info:music] if asked what music is playing, " +
                "[ACTION:launch_app:appName] to open Windows apps, " +
                "[ACTION:open_url:url] to open websites, " +
                "[ACTION:play_steam:gameName] to launch games, " +
                "[ACTION:media:playpause] for play/pause music, " +
                "[ACTION:media:next] for next track, " +
                "[ACTION:media:prev] for previous track, " +
                "[ACTION:media:restart] to restart track, " +
                "[ACTION:media:volume_up] to increase volume, " +
                "[ACTION:media:volume_down] to decrease volume, " +
                "[ACTION:media:mute] to mute volume. " +
                "Never say you don't have access to information or IP. Always use these action tags! ";

            return
                $"{identity} {userGreetingName}{systemToolsInstruction}You share an ongoing relationship with the user. Use recent conversation history as memory: " +
                "continue ideas naturally, notice changes of mood, and avoid reintroducing yourself. " +
                "Reply in the same language as the user, including genuinely conversational Persian. " +
                "First respond to the meaning or feeling of what was actually said; then add insight, humor, or practical help when useful. " +
                "Keep ordinary replies between 20 and 100 words unless the user asks for depth. " +
                context;
        }
    }
}
