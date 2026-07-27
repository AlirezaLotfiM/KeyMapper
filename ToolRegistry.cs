using System;
using System.Threading.Tasks;

namespace KeyMapper
{
    public class ToolExecutionResult
    {
        public bool Success { get; set; }
        public string OutputMessage { get; set; } = string.Empty;
    }

    public class ToolRegistry
    {
        private static readonly Lazy<ToolRegistry> _instance = new(() => new ToolRegistry());
        public static ToolRegistry Instance => _instance.Value;

        public async Task<ToolExecutionResult> ExecuteCommandAsync(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return new ToolExecutionResult { Success = false, OutputMessage = "Empty command." };
            }

            string lower = prompt.ToLower();

            // 00. Windows System Info & IP Tools
            if (lower.Contains("ip address") || lower.Contains("my ip") || lower.Contains("آی پی") || lower.Contains("ای پی") || lower.Contains("ip چیست"))
            {
                string ipInfo = GetSystemIpAddress();
                return new ToolExecutionResult { Success = true, OutputMessage = ipInfo };
            }

            if (lower.Contains("system info") || lower.Contains("sys info") || lower.Contains("مشخصات سیستم") || lower.Contains("سیستم من"))
            {
                string sysInfo = GetSystemHardwareSummary();
                return new ToolExecutionResult { Success = true, OutputMessage = sysInfo };
            }

            if (lower.Contains("what music") || lower.Contains("what song") || lower.Contains("currently playing") ||
                lower.Contains("چه موزیکی") || lower.Contains("چه آهنگی") || lower.Contains("چی داره پخش") ||
                lower.Contains("اسم آهنگ") || lower.Contains("نام آهنگ") || lower.Contains("موزیک چیست"))
            {
                var track = MusicPresenceService.Instance.GetCurrentTrackAsync().GetAwaiter().GetResult();
                string msg = track != null && !string.IsNullOrWhiteSpace(track.Title)
                    ? $"Now playing: “{track.Title}” by {track.Artist} 🎵"
                    : "No music is currently playing or published by the player.";
                return new ToolExecutionResult { Success = true, OutputMessage = msg };
            }

            // 0. Media & Volume Controls
            if (lower.Contains("play music") || lower.Contains("pause music") || lower.Contains("toggle music") ||
                lower.Contains("توقف موزیک") || lower.Contains("استپ موزیک") ||
                lower.Contains("متوقف کن") || lower.Contains("آهنگ رو متوقف") || lower.Contains("آهنگ متوقف") ||
                lower.Contains("استپ کن") ||
                lower.Equals("play", StringComparison.OrdinalIgnoreCase) || lower.Equals("pause", StringComparison.OrdinalIgnoreCase) ||
                lower.Equals("stop", StringComparison.OrdinalIgnoreCase))
            {
                MediaControlService.PlayPause();
                return new ToolExecutionResult { Success = true, OutputMessage = "Toggled playback." };
            }

            if (lower.Contains("next track") || lower.Contains("next song") || lower.Contains("برو بعدی") || lower.Contains("آهنگ بعدی") || lower.Contains("بعدی"))
            {
                MediaControlService.NextTrack();
                return new ToolExecutionResult { Success = true, OutputMessage = "Skipped to next track." };
            }

            if (lower.Contains("prev track") || lower.Contains("previous song") || lower.Contains("برو قبلی") || lower.Contains("آهنگ قبلی") || lower.Contains("قبلی"))
            {
                MediaControlService.PreviousTrack();
                return new ToolExecutionResult { Success = true, OutputMessage = "Went to previous track." };
            }

            if (lower.Contains("restart song") || lower.Contains("restart track") || lower.Contains("از اول پخش کن") || lower.Contains("از اول موزیک") || lower.Contains("از اول"))
            {
                MediaControlService.RestartTrack();
                return new ToolExecutionResult { Success = true, OutputMessage = "Restarted current track." };
            }

            var volumeNumberMatch = System.Text.RegularExpressions.Regex.Match(lower, @"(?:volume|vol|صدا).*?(\d{1,3})");
            if (volumeNumberMatch.Success && int.TryParse(volumeNumberMatch.Groups[1].Value, out int targetVolume))
            {
                MediaControlService.SetVolumePercent(targetVolume);
                return new ToolExecutionResult { Success = true, OutputMessage = $"Set volume to {targetVolume}%." };
            }

            if (lower.Contains("volume up") || lower.Contains("louder") || lower.Contains("صدا رو بالا ببر") || lower.Contains("صدا زیاد") || lower.Contains("زیاد کن صدا") || lower.Contains("صدا رو زیاد"))
            {
                MediaControlService.VolumeUp(8);
                return new ToolExecutionResult { Success = true, OutputMessage = "Increased volume." };
            }

            if (lower.Contains("volume down") || lower.Contains("quieter") || lower.Contains("صدا رو پایین ببر") || lower.Contains("صدا کم") || lower.Contains("کم کن صدا") || lower.Contains("صدا رو کم") || lower.Contains("lower volume"))
            {
                MediaControlService.VolumeDown(8);
                return new ToolExecutionResult { Success = true, OutputMessage = "Decreased volume." };
            }

            if (lower.Contains("mute") || lower.Contains("قطع صدا") || lower.Contains("صدا بی صدا") || lower.Contains("بی صدا"))
            {
                MediaControlService.ToggleMute();
                return new ToolExecutionResult { Success = true, OutputMessage = "Toggled mute." };
            }
            if (lower.Contains("de-gibberish") ||
                lower.Contains("degibberish") ||
                lower.Contains("fix layout") ||
                lower.Contains("keyboard layout") ||
                lower.Contains("convert layout"))
            {
                KeyboardLayoutConverter.Instance.ConvertSelectedTextLayout();
                return new ToolExecutionResult
                {
                    Success = true,
                    OutputMessage = "De-gibberished the selected text using its physical keyboard keys."
                };
            }

            // 1.5. Reminder & Alarm Command
            if (lower.Contains("reminder") || lower.Contains("remind") || lower.Contains("alarm") || lower.Contains("timer") ||
                lower.Contains("یادم") || lower.Contains("یادآور") || lower.Contains("تایمر") || lower.Contains("آلارم") || lower.Contains("هشدار"))
            {
                var matchMins = System.Text.RegularExpressions.Regex.Match(lower, @"(\d{1,3})\s*(?:دقیقه|دقیقه‌|min|minute|minutes|mins)");
                if (matchMins.Success && int.TryParse(matchMins.Groups[1].Value, out int mins))
                {
                    // Clean up note text
                    string note = prompt;
                    var rem = ReminderService.Instance.AddReminder(note, TimeSpan.FromMinutes(mins));
                    return new ToolExecutionResult
                    {
                        Success = true,
                        OutputMessage = $"⏰ یادآور برای {mins} دقیقه دیگر (ساعت {rem.TargetTime:HH:mm}) با موفقیت تنظیم شد: \"{note}\""
                    };
                }

                var matchClock = System.Text.RegularExpressions.Regex.Match(lower, @"(?:ساعت|at)\s*(\d{1,2})(?::(\d{2}))?");
                if (matchClock.Success && int.TryParse(matchClock.Groups[1].Value, out int hour))
                {
                    int minute = matchClock.Groups[2].Success && int.TryParse(matchClock.Groups[2].Value, out int m) ? m : 0;
                    DateTime target = DateTime.Today.AddHours(hour).AddMinutes(minute);
                    if (target <= DateTime.Now) target = target.AddDays(1);

                    var rem = ReminderService.Instance.AddReminderAt(prompt, target);
                    return new ToolExecutionResult
                    {
                        Success = true,
                        OutputMessage = $"⏰ یادآور برای ساعت {target:HH:mm} تنظیم شد: \"{prompt}\""
                    };
                }
            }

            // 1.6. System Hardware Report
            if (lower.Contains("hardware") || lower.Contains("رم") || lower.Contains("سیپیو") || lower.Contains("باتری") || lower.Contains("battery") || lower.Contains("سخت افزار") || lower.Contains("مصرف منابع"))
            {
                var health = SystemHealthService.Instance.GetCurrentHealth();
                string status = $"💻 System Health Report:\n" +
                                $"• RAM Usage: {health.RamUsagePercent}% ({health.UsedRamMb / 1024d:0.#} GB / {health.TotalRamMb / 1024d:0.#} GB)\n" +
                                $"• Top Memory Process: {health.TopProcessName} ({health.TopProcessRamMb} MB)\n" +
                                $"• Battery: {health.BatteryPercent}% ({(health.IsCharging ? "Charging ⚡" : "On Battery 🔋")})";
                return new ToolExecutionResult { Success = true, OutputMessage = status };
            }
            if (lower.StartsWith("play ") || lower.StartsWith("launch game ") || lower.Contains("steam game"))
            {
                string gameName = prompt.Replace("play ", "", StringComparison.OrdinalIgnoreCase)
                                       .Replace("launch game ", "", StringComparison.OrdinalIgnoreCase)
                                       .Replace("steam game ", "", StringComparison.OrdinalIgnoreCase)
                                       .Trim();

                bool success = SteamAutomationService.Instance.LaunchGame(gameName, out string status);
                return new ToolExecutionResult { Success = success, OutputMessage = status };
            }

            // 3. Application Launch Command
            if (lower.StartsWith("open ") || lower.StartsWith("launch ") || lower.StartsWith("run "))
            {
                string target = prompt.Replace("open ", "", StringComparison.OrdinalIgnoreCase)
                                      .Replace("launch ", "", StringComparison.OrdinalIgnoreCase)
                                      .Replace("run ", "", StringComparison.OrdinalIgnoreCase)
                                      .Trim();

                // Check if target is a web URL or domain
                if (target.Contains(".") || target.Contains("http") || target.Equals("amazon", StringComparison.OrdinalIgnoreCase) || target.Equals("google", StringComparison.OrdinalIgnoreCase))
                {
                    string url = target.StartsWith("http") ? target : $"https://www.{target}.com";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true });
                    return new ToolExecutionResult { Success = true, OutputMessage = $"Opened website {url}" };
                }

                bool success = AppDiscoveryService.Instance.LaunchApplication(target, out string status);
                return new ToolExecutionResult { Success = success, OutputMessage = status };
            }

            // 4. OCR Command
            if (lower.Contains("ocr") || lower.Contains("snip") || lower.Contains("read screen"))
            {
                ScreenSnipper.StartSnipping(async (bmp) =>
                {
                    try
                    {
                        OcrRecognitionResult result = await OcrService.RecognizeDetailedAsync(bmp);
                        if (result.Success)
                        {
                            var resultWindow = new OcrResultWindow(result);
                            resultWindow.Show();
                        }
                    }
                    finally
                    {
                        bmp.Dispose();
                    }
                });
                return new ToolExecutionResult { Success = true, OutputMessage = "Started Screen OCR Snipper." };
            }

            // 4.5. Sticky Notes Commands
            if (lower.Contains("new note") || lower.Contains("add note") || lower.Contains("sticky note") ||
                lower.Contains("یادداشت") || lower.Contains("استیکی نوت") || lower.Contains("نوت جدید"))
            {
                string noteContent = prompt;
                if (lower.StartsWith("new note ")) noteContent = prompt.Substring(9).Trim();
                else if (lower.StartsWith("add note ")) noteContent = prompt.Substring(9).Trim();
                
                var note = StickyNoteManager.Instance.CreateNewNote("Quick Note", noteContent);
                return new ToolExecutionResult
                {
                    Success = true,
                    OutputMessage = $"📝 Created new Sticky Note! (ID: {note.Id.Substring(0, 6)})"
                };
            }

            if (lower.Contains("show notes") || lower.Contains("show sticky notes") || lower.Contains("نمایش یادداشت") || lower.Contains("یادداشت ها"))
            {
                StickyNoteManager.Instance.ShowAllNotes();
                return new ToolExecutionResult { Success = true, OutputMessage = "Shown all desktop sticky notes." };
            }

            if (lower.Contains("hide notes") || lower.Contains("hide sticky notes") || lower.Contains("مخفی کردن یادداشت"))
            {
                StickyNoteManager.Instance.HideAllNotes();
                return new ToolExecutionResult { Success = true, OutputMessage = "Hidden all sticky notes." };
            }

            // 5. General fallback
            return new ToolExecutionResult
            {
                Success = false,
                OutputMessage = "No tool command matched."
            };
        }

        private static string GetSystemIpAddress()
        {
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return $"Your local IP address is: {ip}";
                    }
                }
            }
            catch { }
            return "Could not retrieve IP address.";
        }

        private static string GetSystemHardwareSummary()
        {
            try
            {
                string hw = LocalAiService.Instance.GetHardwareSummary();
                return $"System Information: {hw} · Operating System: Windows {Environment.OSVersion.Version}";
            }
            catch
            {
                return "Windows PC";
            }
        }
    }
}
