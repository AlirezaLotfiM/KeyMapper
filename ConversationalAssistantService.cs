using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace KeyMapper
{
    internal enum AssistantActionKind
    {
        OpenUrl,
        ConfirmInstall,
        InstallPackage
    }

    internal sealed record AssistantAction(
        string Label,
        AssistantActionKind Kind,
        string Payload,
        bool IsPrimary = false);

    internal sealed record AssistantReply(
        string Message,
        IReadOnlyList<AssistantAction> Actions,
        bool IsPersian);

    internal sealed record KnownApplication(
        string Name,
        string WingetId,
        string OfficialUrl,
        string[] Aliases);

    internal sealed class ConversationalAssistantService
    {
        public static ConversationalAssistantService Instance { get; } = new();

        private static readonly KnownApplication[] KnownApplications =
        [
            new("Steam", "Valve.Steam", "https://store.steampowered.com/about/",
                ["steam", "استیم"]),
            new("Discord", "Discord.Discord", "https://discord.com/download",
                ["discord", "دیسکورد"]),
            new("Telegram", "Telegram.TelegramDesktop", "https://desktop.telegram.org/",
                ["telegram", "تلگرام"]),
            new("Google Chrome", "Google.Chrome", "https://www.google.com/chrome/",
                ["chrome", "google chrome", "کروم", "گوگل کروم"]),
            new("Mozilla Firefox", "Mozilla.Firefox", "https://www.mozilla.org/firefox/new/",
                ["firefox", "mozilla firefox", "فایرفاکس"]),
            new("Spotify", "Spotify.Spotify", "https://www.spotify.com/download/",
                ["spotify", "اسپاتیفای"]),
            new("Visual Studio Code", "Microsoft.VisualStudioCode",
                "https://code.visualstudio.com/download",
                ["visual studio code", "vs code", "vscode", "ویژوال استودیو کد"])
        ];

        private static readonly string[] WebsiteWords =
        [
            "website", "web page", "webpage", "site", "official page", "download page",
            "وبسایت", "وب سایت", "سایت", "صفحه وب", "پیج", "صفحه دانلود"
        ];

        private static readonly string[] GameWords =
        [
            "play ", "launch game", "open game", "steam game", "game ",
            "بازی", "اجراش کن", "لانچ"
        ];

        private ConversationalAssistantService()
        {
        }

        public async Task<AssistantReply> ProcessAsync(
            string prompt,
            string characterName,
            string visibleContext = "",
            IReadOnlyList<ConversationTurn>? history = null)
        {
            string normalized = NormalizePersian(prompt).Trim();
            bool persian = ContainsPersian(normalized);
            if (normalized.Length == 0)
            {
                return new AssistantReply(
                    persian ? "یک چیزی بهم بگو تا انجامش بدهم." : "Tell me what you want me to do.",
                    [],
                    persian);
            }

            KnownApplication? knownApp = FindKnownApplication(normalized);
            bool websiteIntent = ContainsAny(normalized, WebsiteWords);
            bool launchIntent = IsLaunchIntent(normalized);
            string target = ExtractTarget(normalized, knownApp);

            if (knownApp?.Name == "Steam" && websiteIntent)
            {
                OpenUrl("https://store.steampowered.com/");
                return Reply(
                    characterName,
                    "website-opened",
                    persian,
                    "Steam");
            }

            bool cyberpunkMentioned =
                normalized.Contains("cyberpunk", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("سایبرپانک", StringComparison.OrdinalIgnoreCase) ||
                normalized.Contains("سایبر پانک", StringComparison.OrdinalIgnoreCase);
            bool gameIntent = cyberpunkMentioned ||
                              ContainsAny(normalized, GameWords) ||
                              SteamAutomationService.Instance.FindInstalledGame(target) != null;
            if (launchIntent && gameIntent)
            {
                return HandleGame(target, characterName, persian);
            }

            if (launchIntent && knownApp != null)
            {
                return HandleKnownApplication(
                    knownApp,
                    characterName,
                    persian);
            }

            if (launchIntent)
            {
                return HandleGeneralApplication(
                    target,
                    characterName,
                    persian);
            }

            if (cyberpunkMentioned)
            {
                return MissingGameReply(
                    "Cyberpunk 2077",
                    characterName,
                    persian);
            }

            AppSettings settings = ConfigManager.Load();
            string? generated = await AiAssistantService.Instance.ProcessConversationAsync(
                prompt,
                characterName,
                visibleContext,
                history ?? [],
                settings);
            return new AssistantReply(
                string.IsNullOrWhiteSpace(generated)
                    ? OfflineConversation(prompt, characterName, persian)
                    : generated,
                [],
                persian);
        }

        public async Task<AssistantReply> ExecuteActionAsync(
            AssistantAction action,
            string characterName,
            bool persian)
        {
            switch (action.Kind)
            {
                case AssistantActionKind.OpenUrl:
                    OpenUrl(action.Payload);
                    return new AssistantReply(
                        persian
                            ? "صفحه را در مرورگر باز کردم."
                            : "I opened that page in your browser.",
                        [],
                        persian);

                case AssistantActionKind.ConfirmInstall:
                {
                    string[] parts = action.Payload.Split('|', 2);
                    string packageId = parts[0];
                    string displayName = parts.Length > 1 ? parts[1] : packageId;
                    string systemDescription = DescribeCurrentSystem(persian);
                    return new AssistantReply(
                        persian
                            ? $"سیستم تو {systemDescription} است. WinGet نسخه سازگار {displayName} را انتخاب می‌کند. نصبش کنم؟ ممکن است ویندوز تأیید دسترسی بخواهد."
                            : $"You are on {systemDescription}. WinGet will select the compatible {displayName} installer. Install it now? Windows may ask you to approve elevation.",
                        [
                            new AssistantAction(
                                persian ? $"بله، {displayName} را نصب کن" : $"Yes, install {displayName}",
                                AssistantActionKind.InstallPackage,
                                $"{packageId}|{displayName}",
                                true)
                        ],
                        persian);
                }

                case AssistantActionKind.InstallPackage:
                {
                    string[] parts = action.Payload.Split('|', 2);
                    string packageId = parts[0];
                    string displayName = parts.Length > 1 ? parts[1] : packageId;
                    return await InstallPackageAsync(
                        packageId,
                        displayName,
                        characterName,
                        persian);
                }

                default:
                    return new AssistantReply(
                        persian ? "این کار را نشناختم." : "I did not recognize that action.",
                        [],
                        persian);
            }
        }

        private static AssistantReply HandleKnownApplication(
            KnownApplication app,
            string characterName,
            bool persian)
        {
            if (app.Name == "Steam")
            {
                if (SteamAutomationService.Instance.LaunchSteamClient(out _))
                    return Reply(characterName, "app-launched", persian, app.Name);
            }
            else if (AppDiscoveryService.Instance.LaunchApplication(app.Name, out _))
            {
                return Reply(characterName, "app-launched", persian, app.Name);
            }

            return new AssistantReply(
                MissingApplicationMessage(characterName, app.Name, persian),
                [
                    new AssistantAction(
                        persian ? $"نصب {app.Name}" : $"Install {app.Name}",
                        AssistantActionKind.ConfirmInstall,
                        $"{app.WingetId}|{app.Name}",
                        true),
                    new AssistantAction(
                        persian ? "صفحه رسمی دانلود" : "Official download page",
                        AssistantActionKind.OpenUrl,
                        app.OfficialUrl)
                ],
                persian);
        }

        private static AssistantReply HandleGeneralApplication(
            string target,
            string characterName,
            bool persian)
        {
            InstalledAppInfo? app = AppDiscoveryService.Instance.FindApplication(target);
            if (app != null &&
                AppDiscoveryService.Instance.LaunchApplication(app.Name, out _))
            {
                return Reply(characterName, "app-launched", persian, app.Name);
            }

            string query = target.Length == 0 ? "application" : target;
            return new AssistantReply(
                MissingApplicationMessage(characterName, query, persian),
                [
                    new AssistantAction(
                        persian ? "جست‌وجوی وب" : "Search the web",
                        AssistantActionKind.OpenUrl,
                        $"https://www.google.com/search?q={Uri.EscapeDataString(query + " official download")}",
                        true)
                ],
                persian);
        }

        private static AssistantReply HandleGame(
            string target,
            string characterName,
            bool persian)
        {
            string gameName = NormalizeGameName(target);
            SteamGameInfo? game = SteamAutomationService.Instance.FindInstalledGame(gameName);
            if (game != null &&
                SteamAutomationService.Instance.LaunchGame(game.Name, out _))
            {
                return Reply(characterName, "game-launched", persian, game.Name);
            }

            return MissingGameReply(gameName, characterName, persian);
        }

        private static AssistantReply MissingGameReply(
            string gameName,
            string characterName,
            bool persian)
        {
            string storeUrl =
                $"https://store.steampowered.com/search/?term={Uri.EscapeDataString(gameName)}";
            string webUrl =
                $"https://www.google.com/search?q={Uri.EscapeDataString(gameName + " game")}";
            return new AssistantReply(
                MissingGameMessage(characterName, gameName, persian),
                [
                    new AssistantAction(
                        persian ? "نمایش در فروشگاه استیم" : "Show in Steam store",
                        AssistantActionKind.OpenUrl,
                        storeUrl,
                        true),
                    new AssistantAction(
                        persian ? "درباره بازی جست‌وجو کن" : "Search about the game",
                        AssistantActionKind.OpenUrl,
                        webUrl)
                ],
                persian);
        }

        private static async Task<AssistantReply> InstallPackageAsync(
            string packageId,
            string displayName,
            string characterName,
            bool persian)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "winget",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("install");
                startInfo.ArgumentList.Add("--id");
                startInfo.ArgumentList.Add(packageId);
                startInfo.ArgumentList.Add("--exact");
                startInfo.ArgumentList.Add("--source");
                startInfo.ArgumentList.Add("winget");
                startInfo.ArgumentList.Add("--silent");
                startInfo.ArgumentList.Add("--accept-package-agreements");
                startInfo.ArgumentList.Add("--accept-source-agreements");
                startInfo.ArgumentList.Add("--disable-interactivity");

                using Process process = Process.Start(startInfo)
                    ?? throw new InvalidOperationException("WinGet could not be started.");
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                    detail = LastUsefulLine(detail);
                    return new AssistantReply(
                        persian
                            ? $"نصب {displayName} کامل نشد. {detail}"
                            : $"{displayName} was not installed. {detail}",
                        [],
                        persian);
                }

                if (displayName.Equals("Steam", StringComparison.OrdinalIgnoreCase))
                    SteamAutomationService.Instance.LaunchSteamClient(out _);
                else
                    AppDiscoveryService.Instance.LaunchApplication(
                        AppDiscoveryService.Instance.FindApplication(
                            displayName,
                            true)?.Name ?? displayName,
                        out _);

                return new AssistantReply(
                    persian
                        ? $"{displayName} نصب شد و بازش کردم. ورود به حساب با خودت است."
                        : $"{displayName} is installed and I opened it. I’ll leave account login to you.",
                    [],
                    persian);
            }
            catch (Exception ex)
            {
                return new AssistantReply(
                    persian
                        ? $"نتوانستم نصب را شروع کنم: {ex.Message}"
                        : $"I could not start the installation: {ex.Message}",
                    [],
                    persian);
            }
        }

        private static KnownApplication? FindKnownApplication(string prompt) =>
            KnownApplications.FirstOrDefault(app =>
                app.Aliases.Any(alias =>
                    prompt.Contains(alias, StringComparison.OrdinalIgnoreCase)));

        private static bool IsLaunchIntent(string prompt) =>
            ContainsAny(prompt,
            [
                "open", "launch", "run", "play", "start",
                "باز کن", "بازش کن", "اجرا کن", "اجراش کن", "بالا بیار", "راه بنداز"
            ]);

        private static string ExtractTarget(string prompt, KnownApplication? knownApp)
        {
            if (knownApp != null) return knownApp.Name;

            string target = prompt;
            string[] removable =
            [
                "please", "for me", "check", "open", "launch", "run", "play", "start",
                "game", "steam game", "لطفا", "لطفاً", "برای من", "چک کن", "ببین",
                "بازش کن", "باز کن", "اجراش کن", "اجرا کن", "بالا بیار", "راه بنداز",
                "بازی", "رو", "را"
            ];
            foreach (string phrase in removable)
            {
                target = Regex.Replace(
                    target,
                    Regex.Escape(phrase),
                    " ",
                    RegexOptions.IgnoreCase);
            }
            return Regex.Replace(target, @"\s+", " ").Trim(' ', '.', '!', '؟', '?');
        }

        private static string NormalizeGameName(string target)
        {
            if (target.Contains("cyberpunk", StringComparison.OrdinalIgnoreCase) ||
                target.Contains("سایبرپانک", StringComparison.OrdinalIgnoreCase) ||
                target.Contains("سایبر پانک", StringComparison.OrdinalIgnoreCase))
                return "Cyberpunk 2077";
            return target;
        }

        private static string NormalizePersian(string value) =>
            value.Replace('\u064A', '\u06CC').Replace('\u0643', '\u06A9');

        private static bool ContainsPersian(string value) =>
            value.Any(character => character is >= '\u0600' and <= '\u06FF');

        private static bool ContainsAny(string value, IEnumerable<string> needles) =>
            needles.Any(needle =>
                value.Contains(needle, StringComparison.OrdinalIgnoreCase));

        private static void OpenUrl(string url) =>
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });

        private static string DescribeCurrentSystem(bool persian)
        {
            string architecture = RuntimeInformation.OSArchitecture switch
            {
                Architecture.X64 => "64-bit (x64)",
                Architecture.X86 => "32-bit (x86)",
                Architecture.Arm64 => "64-bit ARM",
                Architecture.Arm => "32-bit ARM",
                _ => RuntimeInformation.OSArchitecture.ToString()
            };
            return persian ? $"ویندوز {architecture}" : $"Windows {architecture}";
        }

        private static string LastUsefulLine(string text) =>
            text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .LastOrDefault(line => line.Length > 0)
                ?? "WinGet returned an unknown error.";

        private static AssistantReply Reply(
            string character,
            string eventName,
            bool persian,
            string target)
        {
            string message = (character, eventName, persian) switch
            {
                ("Pink Monster", "app-launched", false) =>
                    $"Found {target}! I gave it a little push and it is opening now.",
                ("Pink Monster", "app-launched", true) =>
                    $"{target} رو پیدا کردم! یه هل کوچولو دادم و الان داره باز می‌شه.",
                ("Pink Monster", "game-launched", false) =>
                    $"{target} is installed—game time! I sent it to Steam.",
                ("Pink Monster", "game-launched", true) =>
                    $"{target} نصبه—وقت بازیه! فرستادمش برای اجرا در استیم.",
                ("Pink Monster", "website-opened", false) =>
                    $"Hop! {target}'s website is open in your browser.",
                ("Pink Monster", "website-opened", true) =>
                    $"هاپ! سایت {target} رو توی مرورگرت باز کردم.",

                ("Owlet Monster", "app-launched", false) =>
                    $"{target} is installed. I have launched the verified local executable.",
                ("Owlet Monster", "app-launched", true) =>
                    $"{target} نصب است. فایل اجرایی محلیِ تأییدشده را باز کردم.",
                ("Owlet Monster", "game-launched", false) =>
                    $"{target} is present in a Steam library. Launch request submitted.",
                ("Owlet Monster", "game-launched", true) =>
                    $"{target} در کتابخانه استیم پیدا شد. درخواست اجرا ارسال شد.",
                ("Owlet Monster", "website-opened", false) =>
                    $"The official {target} page is now open.",
                ("Owlet Monster", "website-opened", true) =>
                    $"صفحه رسمی {target} اکنون باز است.",

                (_, "app-launched", false) =>
                    $"{target} is installed. Opening it now.",
                (_, "app-launched", true) =>
                    $"{target} نصبه. الان بازش می‌کنم.",
                (_, "game-launched", false) =>
                    $"{target} is installed. Launching through Steam.",
                (_, "game-launched", true) =>
                    $"{target} نصبه. از طریق استیم اجراش می‌کنم.",
                (_, "website-opened", false) =>
                    $"{target} website opened. Done.",
                (_, "website-opened", true) =>
                    $"سایت {target} باز شد. انجام شد.",
                _ => persian ? "انجام شد." : "Done."
            };
            return new AssistantReply(message, [], persian);
        }

        private static string MissingApplicationMessage(
            string character,
            string app,
            bool persian) =>
            (character, persian) switch
            {
                ("Pink Monster", false) =>
                    $"Uh-oh—{app} is not hiding anywhere on this PC. Want me to fetch the right installer?",
                ("Pink Monster", true) =>
                    $"اوه اوه—{app} هیچ‌جای این کامپیوتر قایم نشده! می‌خوای نصب‌کننده درستش رو برات بگیرم؟",
                ("Owlet Monster", false) =>
                    $"I checked the installed-application records; {app} is absent. I can use its verified package or official page.",
                ("Owlet Monster", true) =>
                    $"فهرست برنامه‌های نصب‌شده را بررسی کردم؛ {app} موجود نیست. می‌توانم از بسته معتبر یا صفحه رسمی استفاده کنم.",
                (_, false) =>
                    $"You do not have {app}. I can install it or open the official download page.",
                (_, true) =>
                    $"{app} رو نداری. می‌تونم نصبش کنم یا صفحه رسمی دانلود رو باز کنم."
            };

        private static string MissingGameMessage(
            string character,
            string game,
            bool persian) =>
            (character, persian) switch
            {
                ("Pink Monster", false) =>
                    $"I searched every Steam shelf—no {game}. Want the store page, or should we investigate the game first?",
                ("Pink Monster", true) =>
                    $"همه قفسه‌های استیم رو گشتم—{game} نبود! صفحه فروشگاه رو می‌خوای یا اول درباره بازی جست‌وجو کنیم؟",
                ("Owlet Monster", false) =>
                    $"{game} is not installed in any detected Steam library. I can open its store listing or research page.",
                ("Owlet Monster", true) =>
                    $"{game} در هیچ کتابخانه استیم شناسایی‌شده‌ای نصب نیست. می‌توانم صفحه فروشگاه یا اطلاعات بازی را باز کنم.",
                (_, false) =>
                    $"{game} is not installed. Store page or web search?",
                (_, true) =>
                    $"{game} نصب نیست. فروشگاه استیم یا جست‌وجوی وب؟"
            };

        private static string OfflineConversation(
            string prompt,
            string character,
            bool persian)
        {
            string normalized = NormalizePersian(prompt);
            bool asksHelloInEnglish =
                normalized.Contains("سلام", StringComparison.Ordinal) &&
                normalized.Contains("انگلیسی", StringComparison.Ordinal) &&
                ContainsAny(normalized, ["چی", "چه", "معنی", "ترجمه"]);
            if (asksHelloInEnglish)
            {
                return character switch
                {
                    "Pink Monster" =>
                        "«سلام» به انگلیسی می‌شود “hello”. اگر خودمانی‌تر بخواهی، “hi” هم خوب است!",
                    "Owlet Monster" =>
                        "«سلام» در انگلیسی “hello” است؛ در گفت‌وگوی خودمانی می‌توانی از “hi” نیز استفاده کنی.",
                    _ =>
                        "«سلام» یعنی “hello”. خودمانی‌ترش می‌شود “hi”."
                };
            }

            bool greeting = Regex.IsMatch(
                normalized,
                @"^\s*(hello|hi|hey|سلام|درود)\s*[!?.،؟]*\s*$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            bool asksHow =
                ContainsAny(
                    normalized,
                    ["how are you", "how do you feel", "حالت چطوره", "خوبی"]);
            bool thanks =
                ContainsAny(normalized, ["thank", "thanks", "مرسی", "ممنون"]);

            if (greeting)
            {
                return (character, persian) switch
                {
                    ("Pink Monster", true) =>
                        "سلام! خوب شد صدایم کردی. امروز ذهنت بیشتر دنبال ساختن است، بازی کردن، یا فقط یک گفت‌وگوی آرام؟",
                    ("Pink Monster", false) =>
                        "Hey! I’m glad you called me over. Is your brain in a making mood, a gaming mood, or a quiet-chat mood today?",
                    ("Owlet Monster", true) =>
                        "درود. امروز با چه جور فکری نشسته‌ای پشت این میز—فکری که باید حل شود یا فکری که فقط باید شنیده شود؟",
                    ("Owlet Monster", false) =>
                        "Good to see you. What sort of thought brought you here today—one that needs solving, or one that simply needs hearing?",
                    (_, true) =>
                        "سلام. من اینجام. امروز چه خبر—کار جدی، بازی، یا فقط حرف زدن؟",
                    _ =>
                        "Hey. I’m here. What kind of day is it—serious work, games, or just talking?"
                };
            }

            if (asksHow)
            {
                return (character, persian) switch
                {
                    ("Pink Monster", true) =>
                        "کمی کنجکاوم، کمی پرانرژی، و راستش خوشحالم که این بار به‌جای دستور دادن حالم را پرسیدی. خودت واقعاً چطوری؟",
                    ("Pink Monster", false) =>
                        "Curious, a little over-energized, and honestly pleased you asked me something instead of giving me a command. How are you—really?",
                    ("Owlet Monster", true) =>
                        "آرام و هوشیارم. البته حالِ یک جغد پیکسلی مفهوم عجیبی است، اما توجه تو آن را واقعی‌تر می‌کند. حال خودت چطور است؟",
                    ("Owlet Monster", false) =>
                        "Calm and attentive. The mood of a pixel owl is a strange idea, admittedly, but your attention makes it feel less abstract. How are you?",
                    (_, true) =>
                        "بد نیستم. سر پا، حواسم جمع، و فعلاً هیچ پنجره‌ای هم با من دعوا ندارد. تو چطوری؟",
                    _ =>
                        "Not bad. Upright, awake, and no windows are fighting me at the moment. How about you?"
                };
            }

            if (thanks)
            {
                return (character, persian) switch
                {
                    ("Pink Monster", true) => "خواهش! این یکی را می‌گذارم توی جیبِ پیروزی‌های کوچک.",
                    ("Pink Monster", false) => "You’re welcome! I’m putting that one in my pocket of tiny victories.",
                    ("Owlet Monster", true) => "خواهش می‌کنم. کمکِ خوب باید بعد از انجام شدن، بی‌سروصدا کنار برود.",
                    ("Owlet Monster", false) => "You are welcome. Good assistance should become quiet once it has done its job.",
                    (_, true) => "قابلی نداشت. بریم سراغ بعدی.",
                    _ => "Any time. On to the next thing."
                };
            }

            return (character, persian) switch
            {
                ("Pink Monster", true) =>
                    "حرفت توجهم را گرفت، ولی در حالت آفلاین نمی‌خواهم یک جواب قلابی از خودم بسازم. اگر سرویس گفت‌وگو را در Settings وصل کنی، می‌توانیم واقعاً همین بحث را ادامه بدهیم.",
                ("Pink Monster", false) =>
                    "That caught my attention, but in offline mode I don’t want to manufacture a fake answer. Connect a conversation service in Settings and we can genuinely keep going with this exact thought.",
                ("Owlet Monster", true) =>
                    "برای این پرسش پاسخ ازپیش‌ساخته شایسته نیست. سرویس گفت‌وگو فعلاً متصل نیست؛ پس ترجیح می‌دهم صادق باشم تا متقاعدکننده به نظر برسم.",
                ("Owlet Monster", false) =>
                    "That deserves more than a prepared answer. The conversation service is not connected, so I would rather be honest than merely sound convincing.",
                (_, true) =>
                    "جواب آماده تحویلت نمی‌دهم. گفت‌وگوی هوشمند هنوز وصل نیست؛ از Settings وصلش کن تا درست درباره‌اش حرف بزنیم.",
                _ =>
                    "I’m not giving you a canned answer. Rich conversation is not connected yet; set it up in Settings and we’ll talk about it properly."
            };
        }

        private static string CapabilityMessage(string character, bool persian) =>
            (character, persian) switch
            {
                ("Pink Monster", false) =>
                    "I can open installed apps, launch Steam games, open official websites, and help install trusted apps. Try: “Open Steam” or “سایبرپانک رو باز کن”.",
                ("Pink Monster", true) =>
                    "می‌تونم برنامه‌های نصب‌شده و بازی‌های استیم رو باز کنم، سایت رسمی رو بیارم و برای نصب برنامه‌های معتبر کمک کنم. مثلاً بگو: «استیم رو باز کن».",
                ("Owlet Monster", false) =>
                    "State an app, game, or website request in Persian or English. I will verify what is installed before proposing the next step.",
                ("Owlet Monster", true) =>
                    "درخواست برنامه، بازی یا وب‌سایت را فارسی یا انگلیسی بگو. ابتدا نصب بودنش را بررسی می‌کنم و بعد پیشنهاد می‌دهم.",
                (_, false) =>
                    "Ask me to open an app, a Steam game, or a website—in Persian or English.",
                (_, true) =>
                    "فارسی یا انگلیسی بگو چه برنامه، بازی استیم یا سایتی رو باز کنم."
            };
    }
}
