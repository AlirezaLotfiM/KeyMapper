using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;

namespace KeyMapper
{
    public class KeyboardLayoutConverter
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static readonly Lazy<KeyboardLayoutConverter> _instance = new(() => new KeyboardLayoutConverter());
        public static KeyboardLayoutConverter Instance => _instance.Value;

        private static readonly Dictionary<char, char> PersianToEnglish = new()
        {
            ['ض'] = 'q', ['ص'] = 'w', ['ث'] = 'e', ['ق'] = 'r', ['ف'] = 't',
            ['غ'] = 'y', ['ع'] = 'u', ['ه'] = 'i', ['خ'] = 'o', ['ح'] = 'p',
            ['ج'] = '[', ['چ'] = ']', ['ش'] = 'a', ['س'] = 's', ['ی'] = 'd',
            ['ي'] = 'd', ['ب'] = 'f', ['ل'] = 'g', ['ا'] = 'h', ['ت'] = 'j',
            ['ن'] = 'k', ['م'] = 'l', ['ک'] = ';', ['ك'] = ';', ['گ'] = '\'',
            ['ظ'] = 'z', ['ط'] = 'x', ['ز'] = 'c', ['ر'] = 'v', ['ذ'] = 'b',
            ['د'] = 'n', ['پ'] = 'm', ['و'] = ',', ['؟'] = '/'
        };

        private static readonly Dictionary<char, char> EnglishToPersian =
            PersianToEnglish
                .Where(pair => pair.Key is not 'ي' and not 'ك')
                .ToDictionary(pair => pair.Value, pair => pair.Key);

        // Physical Keyboard Key Indices to Characters for EN (QWERTY), DE (QWERTZ), FA (ISIRI)
        private static readonly string EnQwerty = "`1234567890-=qwertyuiop[]\\asdfghjkl;'zxcvbnm,./~!@#$%^&*()_+QWERTYUIOP{}|ASDFGHJKL:\"ZXCVBNM<>?";
        private static readonly string DeQwertz = "^1234567890-ßqwertzuiopü+#asdfghjklöäyxcvbnm,.-°!\"§$%&/()=?`QWERTZUIOPÜ'*ASDFGHJKLÖÄYXCVBNM;:_";
        private static readonly string FaIsiri = "`;1234567890-=ضصثقفغعهخحجچ\\شسیبلاتنمکگظطزرذدپo~!@#$%^&*()_+ضصثقفغعهخحجچ|شسیبلاتنمکگظطزرذدپ؟";

        // Dictionary of common words for language likelihood validation
        private static readonly HashSet<string> GermanCommonWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "der", "die", "das", "und", "ist", "in", "den", "zu", "mit", "nicht", "von", "sie", "hallo",
            "mein", "lehrer", "ich", "es", "sich", "auf", "für", "eine", "einen", "haben", "wir", "guten", "tag"
        };

        private static readonly HashSet<string> EnglishCommonWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "the", "be", "to", "of", "and", "a", "in", "that", "have", "i", "it", "for", "not", "on", "with",
            "he", "as", "you", "do", "at", "this", "but", "his", "by", "from", "hello", "teacher", "my"
        };

        private static readonly HashSet<string> PersianCommonWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "و", "در", "به", "از", "که", "این", "را", "با", "است", "برای", "آن", "یک", "خود", "تا", "سلام", "من", "استاد"
        };

        public string ConvertText(string input, out string detectedTargetLanguage)
        {
            detectedTargetLanguage = "Unknown";
            if (string.IsNullOrEmpty(input)) return input;

            // A manual layout-fix action means "interpret these glyphs as physical
            // key positions", not "translate this natural-language sentence".
            if (input.Any(IsPersianCharacter))
            {
                detectedTargetLanguage = "English";
                return MapCharacters(input, PersianToEnglish);
            }

            string fromEnToDe = RemapString(input, EnQwerty, DeQwertz);
            string fromEnToFa = MapCharacters(input, EnglishToPersian);
            string fromDeToEn = RemapString(input, DeQwertz, EnQwerty);
            string fromDeToFa = RemapString(input, DeQwertz, FaIsiri);

            var candidates = new (string text, string targetLang, double score)[]
            {
                (fromEnToDe, "German", EvaluateScore(fromEnToDe, GermanCommonWords)),
                (fromEnToFa, "Persian", EvaluateScore(fromEnToFa, PersianCommonWords)),
                (fromDeToEn, "English", EvaluateScore(fromDeToEn, EnglishCommonWords)),
                (fromDeToFa, "Persian", EvaluateScore(fromDeToFa, PersianCommonWords))
            };

            var bestCandidate = candidates.OrderByDescending(c => c.score).FirstOrDefault();

            if (bestCandidate.score > 0.0)
            {
                detectedTargetLanguage = bestCandidate.targetLang;
                return bestCandidate.text;
            }

            detectedTargetLanguage = "Persian";
            return fromEnToFa;
        }

        private static bool IsPersianCharacter(char character) =>
            character is >= '\u0600' and <= '\u06FF';

        private static string MapCharacters(string input, IReadOnlyDictionary<char, char> map)
        {
            char[] output = input.ToCharArray();
            for (int i = 0; i < output.Length; i++)
            {
                char lookup = char.ToLowerInvariant(output[i]);
                if (map.TryGetValue(lookup, out char mapped))
                {
                    output[i] = char.IsUpper(output[i]) ? char.ToUpperInvariant(mapped) : mapped;
                }
            }
            return new string(output);
        }

        private string RemapString(string source, string fromLayout, string toLayout)
        {
            char[] result = new char[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                int index = fromLayout.IndexOf(source[i]);
                if (index >= 0 && index < toLayout.Length)
                {
                    result[i] = toLayout[index];
                }
                else
                {
                    result[i] = source[i];
                }
            }
            return new string(result);
        }

        private double EvaluateScore(string text, HashSet<string> dictionary)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0.0;

            string[] words = text.Split(new[] { ' ', '\t', '\r', '\n', '.', ',', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return 0.0;

            int matches = words.Count(w => dictionary.Contains(w));
            return (double)matches / words.Length;
        }

        public async Task<(bool Success, string Original, string Corrected, string Message)> ConvertSelectedTextLayoutAsync(IntPtr targetWindow = default)
        {
            string clipboardBackup = string.Empty;
            try
            {
                clipboardBackup = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
                string marker = $"__PET_SELECTION_{Guid.NewGuid():N}__";
                System.Windows.Clipboard.SetText(marker);

                IntPtr destination = targetWindow != IntPtr.Zero ? targetWindow : GetForegroundWindow();
                if (destination != IntPtr.Zero)
                {
                    SetForegroundWindow(destination);
                    await Task.Delay(120);
                }
                // Ctrl+Alt+K can leave the target application's Alt access-key
                // overlay active even after the keys are released. Dismiss that
                // mode without changing the text selection before copying.
                SendKeys.SendWait("{ESC}");
                await Task.Delay(90);
                SendKeys.SendWait("^c");
                await Task.Delay(220);

                if (System.Windows.Clipboard.ContainsText())
                {
                    string highlightedText = System.Windows.Clipboard.GetText();
                    if (!string.IsNullOrWhiteSpace(highlightedText) && highlightedText != marker)
                    {
                        string correctedText = ConvertText(highlightedText, out string targetLang);
                        System.Windows.Clipboard.SetText(correctedText);
                        await Task.Delay(80);
                        SendKeys.SendWait("^v");
                        return (true, highlightedText, correctedText, $"Converted to {targetLang}");
                    }
                }
                if (!string.IsNullOrEmpty(clipboardBackup)) System.Windows.Clipboard.SetText(clipboardBackup);
                else System.Windows.Clipboard.Clear();
                return (false, string.Empty, string.Empty, "No selected text was found.");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Keyboard layout conversion error: {ex.Message}");
                return (false, string.Empty, string.Empty, ex.Message);
            }
        }

        public async Task<(bool Success, string Text)> CaptureSelectedTextAsync(IntPtr targetWindow = default)
        {
            string clipboardBackup = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
            try
            {
                string marker = $"__PET_SELECTION_{Guid.NewGuid():N}__";
                System.Windows.Clipboard.SetText(marker);
                IntPtr destination = targetWindow != IntPtr.Zero ? targetWindow : GetForegroundWindow();
                if (destination != IntPtr.Zero)
                {
                    SetForegroundWindow(destination);
                    await Task.Delay(120);
                }
                SendKeys.SendWait("^c");
                await Task.Delay(220);
                string selected = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : string.Empty;
                return (!string.IsNullOrWhiteSpace(selected) && selected != marker, selected == marker ? string.Empty : selected);
            }
            finally
            {
                if (!string.IsNullOrEmpty(clipboardBackup)) System.Windows.Clipboard.SetText(clipboardBackup);
                else System.Windows.Clipboard.Clear();
            }
        }

        public async void ConvertSelectedTextLayout()
        {
            await ConvertSelectedTextLayoutAsync();
        }
    }
}
