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

            // 1. De-gibberish: reverse accidental physical keyboard layouts.
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

            // 2. Steam Game Launch Command
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

            // 5. General de-gibberish fallback
            string converted = KeyboardLayoutConverter.Instance.ConvertText(prompt, out string targetLang);
            return new ToolExecutionResult
            {
                Success = true,
                OutputMessage = $"Converted to {targetLang}:\n{converted}"
            };
        }
    }
}
