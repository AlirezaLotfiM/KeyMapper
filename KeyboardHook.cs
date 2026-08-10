using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Input;

namespace KeyMapper
{
    public class KeyboardHook : IDisposable
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;

        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_ESCAPE = 0x1B;
        private const int VK_BACK = 0x08;
        private const int VK_RETURN = 0x0D;

        public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        private LowLevelKeyboardProc _proc;
        private IntPtr _hookId = IntPtr.Zero;

        // Recording state
        private bool _lCtrlDown = false;
        private bool _lCtrlUsed = false;
        private bool _deGibberishChordDown;
        private bool _musicPlayerChordDown;
        private bool _commandPaletteChordDown;
        private bool _isRecording = false;
        private StringBuilder _buffer = new StringBuilder();
        private DateTime _lastLCtrlReleaseTime = DateTime.MinValue;
        private readonly StringBuilder _rollingBuffer = new StringBuilder();
        private IntPtr _lastActiveHwnd = IntPtr.Zero;

        // Settings / Callbacks
        public bool IsEnabled { get; set; } = true;
        public bool SuppressKeysDuringRecording { get; set; } = true;
        public List<string> AutoExpandShortcuts { get; set; } = new List<string>();
        public Func<string, bool>? IsShortcutAllowed { get; set; }
        public bool IsPaused { get; set; } = false;
        public bool IsCapturingCommandPaletteHotkey { get; set; }
        public string CommandPaletteHotkey
        {
            get => _commandPaletteGesture.Display;
            set
            {
                if (ConfiguredHotkey.TryParse(value, out ConfiguredHotkey? parsed) &&
                    parsed != null)
                {
                    _commandPaletteGesture = parsed;
                }
            }
        }

        public event Action<bool>? OnPauseToggled;

        public event Action<string>? OnBufferChanged;
        public event Action<string>? OnReplacementTriggered; // Triggers on Right Ctrl
        public event Action<string>? OnActionTriggered;      // Triggers on Right Shift
        public event Action? OnRecordingCancelled;
        public event Action? OnDoubleTapLCtrl;
        public event Action<IntPtr>? OnDeGibberishRequested;
        public event Action? OnMusicPlayerRequested;
        public event Action<string, string>? OnAutoExpandTriggered;

        private ConfiguredHotkey _commandPaletteGesture =
            ConfiguredHotkey.DoubleLeftCtrl();

        private sealed class ConfiguredHotkey
        {
            private ConfiguredHotkey(
                string display,
                int virtualKey,
                bool control,
                bool alt,
                bool shift,
                bool windows,
                bool isDisabled,
                bool isDoubleLeftCtrl)
            {
                Display = display;
                VirtualKey = virtualKey;
                Control = control;
                Alt = alt;
                Shift = shift;
                Windows = windows;
                IsDisabled = isDisabled;
                IsDoubleLeftCtrl = isDoubleLeftCtrl;
            }

            public string Display { get; }
            public int VirtualKey { get; }
            public bool Control { get; }
            public bool Alt { get; }
            public bool Shift { get; }
            public bool Windows { get; }
            public bool IsDisabled { get; }
            public bool IsDoubleLeftCtrl { get; }

            public static ConfiguredHotkey DoubleLeftCtrl() =>
                new("Double Left Ctrl", VK_LCONTROL, false, false, false, false, false, true);

            private static ConfiguredHotkey Disabled() =>
                new("Disabled", 0, false, false, false, false, true, false);

            public bool ModifiersMatch()
            {
                bool control = IsControlPressed();
                bool alt = IsAltPressed();
                bool shift = IsShiftPressed();
                bool windows =
                    (GetKeyState(0x5B) & 0x8000) != 0 ||
                    (GetKeyState(0x5C) & 0x8000) != 0;
                return control == Control &&
                       alt == Alt &&
                       shift == Shift &&
                       windows == Windows;
            }

            public static bool TryParse(
                string? value,
                out ConfiguredHotkey? parsed)
            {
                string text = value?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(text) ||
                    text.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Off", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("None", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = Disabled();
                    return true;
                }

                if (text.Equals("Double Left Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    text.Equals("Double Ctrl", StringComparison.OrdinalIgnoreCase))
                {
                    parsed = DoubleLeftCtrl();
                    return true;
                }

                string[] tokens = text.Split(
                    '+',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (tokens.Length == 0)
                {
                    parsed = null;
                    return false;
                }

                bool control = false;
                bool alt = false;
                bool shift = false;
                bool windows = false;
                int virtualKey = 0;
                for (int index = 0; index < tokens.Length; index++)
                {
                    string compact = tokens[index]
                        .Replace(" ", string.Empty, StringComparison.Ordinal)
                        .Replace("-", string.Empty, StringComparison.Ordinal)
                        .ToUpperInvariant();
                    if (index < tokens.Length - 1)
                    {
                        switch (compact)
                        {
                            case "CTRL":
                            case "CONTROL":
                                if (control) { parsed = null; return false; }
                                control = true;
                                break;
                            case "ALT":
                                if (alt) { parsed = null; return false; }
                                alt = true;
                                break;
                            case "SHIFT":
                                if (shift) { parsed = null; return false; }
                                shift = true;
                                break;
                            case "WIN":
                            case "WINDOWS":
                            case "META":
                                if (windows) { parsed = null; return false; }
                                windows = true;
                                break;
                            default:
                                parsed = null;
                                return false;
                        }
                    }
                    else if (!TryParseVirtualKey(compact, out virtualKey))
                    {
                        parsed = null;
                        return false;
                    }
                }

                parsed = new ConfiguredHotkey(
                    FormatDisplay(virtualKey, control, alt, shift, windows),
                    virtualKey,
                    control,
                    alt,
                    shift,
                    windows,
                    false,
                    false);
                return true;
            }

            private static bool TryParseVirtualKey(
                string token,
                out int virtualKey)
            {
                if (token.Length == 1 &&
                    ((token[0] >= 'A' && token[0] <= 'Z') ||
                     (token[0] >= '0' && token[0] <= '9')))
                {
                    virtualKey = token[0];
                    return true;
                }

                if (token.StartsWith("F", StringComparison.Ordinal) &&
                    int.TryParse(token[1..], out int functionNumber) &&
                    functionNumber is >= 1 and <= 24)
                {
                    virtualKey = 0x70 + functionNumber - 1;
                    return true;
                }

                virtualKey = token switch
                {
                    "SPACE" => 0x20,
                    "TAB" => 0x09,
                    "ENTER" or "RETURN" => 0x0D,
                    "ESC" or "ESCAPE" => 0x1B,
                    "BACKSPACE" => 0x08,
                    "INSERT" => 0x2D,
                    "DELETE" or "DEL" => 0x2E,
                    "HOME" => 0x24,
                    "END" => 0x23,
                    "PAGEUP" => 0x21,
                    "PAGEDOWN" => 0x22,
                    "LEFT" => 0x25,
                    "UP" => 0x26,
                    "RIGHT" => 0x27,
                    "DOWN" => 0x28,
                    "PRINTSCREEN" or "PRTSC" => 0x2C,
                    "SCROLLLOCK" => 0x91,
                    "PAUSE" => 0x13,
                    _ => 0
                };
                return virtualKey != 0;
            }

            private static string FormatDisplay(
                int virtualKey,
                bool control,
                bool alt,
                bool shift,
                bool windows)
            {
                var parts = new List<string>();
                if (control) parts.Add("Ctrl");
                if (alt) parts.Add("Alt");
                if (shift) parts.Add("Shift");
                if (windows) parts.Add("Win");
                parts.Add(virtualKey switch
                {
                    0x20 => "Space",
                    0x09 => "Tab",
                    0x0D => "Enter",
                    0x1B => "Esc",
                    0x08 => "Backspace",
                    0x2D => "Insert",
                    0x2E => "Delete",
                    0x24 => "Home",
                    0x23 => "End",
                    0x21 => "PageUp",
                    0x22 => "PageDown",
                    0x25 => "Left",
                    0x26 => "Up",
                    0x27 => "Right",
                    0x28 => "Down",
                    0x2C => "PrintScreen",
                    0x91 => "ScrollLock",
                    0x13 => "Pause",
                    >= 0x70 and <= 0x87 => $"F{virtualKey - 0x70 + 1}",
                    _ => ((char)virtualKey).ToString()
                });
                return string.Join("+", parts);
            }
        }

        public KeyboardHook()
        {
            _proc = HookCallback;
        }

        public void Hook()
        {
            if (_hookId == IntPtr.Zero)
            {
                _hookId = SetHook(_proc);
            }
        }

        public void Unhook()
        {
            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
        }

        private IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule? curModule = curProcess.MainModule)
            {
                if (curModule == null || string.IsNullOrEmpty(curModule.ModuleName)) return IntPtr.Zero;
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc,
                    GetModuleHandle(curModule.ModuleName), 0);
            }
        }

        public void CancelRecording()
        {
            if (_isRecording)
            {
                _isRecording = false;
                _buffer.Clear();
                OnRecordingCancelled?.Invoke();
                OnBufferChanged?.Invoke(string.Empty);
            }
        }

        private static bool IsControlPressed()
        {
            return (GetKeyState(0x11) & 0x8000) != 0; // VK_CONTROL = 0x11
        }

        private static bool IsAltPressed()
        {
            return (GetKeyState(0x12) & 0x8000) != 0; // VK_MENU = 0x12 (Alt)
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && IsEnabled)
            {
                IntPtr activeHwnd = GetForegroundWindow();
                if (activeHwnd != _lastActiveHwnd)
                {
                    _lastActiveHwnd = activeHwnd;
                    _rollingBuffer.Clear();
                }

                int vkCode = Marshal.ReadInt32(lParam);
                int message = wParam.ToInt32();

                if (!_commandPaletteGesture.IsDoubleLeftCtrl &&
                    TryHandleConfiguredCommandPaletteHotkey(vkCode, message))
                {
                    return (IntPtr)1;
                }

                // Ctrl+Alt+K: reverse text typed under the wrong physical keyboard
                // layout. The foreground handle is captured before our UI appears.
                bool isDeGibberishChord = vkCode == 0x4B && IsControlPressed() && IsAltPressed();
                if (isDeGibberishChord && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
                {
                    if (!_deGibberishChordDown)
                    {
                        _deGibberishChordDown = true;
                        CancelRecording();
                        _rollingBuffer.Clear();
                        OnDeGibberishRequested?.Invoke(activeHwnd);
                    }
                    return (IntPtr)1;
                }
                if (vkCode == 0x4B && (message == WM_KEYUP || message == WM_SYSKEYUP))
                {
                    _deGibberishChordDown = false;
                }

                // Ctrl+Alt+M: Show/Toggle Music Player Widget
                bool isMusicPlayerChord = vkCode == 0x4D && IsControlPressed() && IsAltPressed();
                if (isMusicPlayerChord && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
                {
                    if (!_musicPlayerChordDown)
                    {
                        _musicPlayerChordDown = true;
                        CancelRecording();
                        _rollingBuffer.Clear();
                        OnMusicPlayerRequested?.Invoke();
                    }
                    return (IntPtr)1;
                }
                if (vkCode == 0x4D && (message == WM_KEYUP || message == WM_SYSKEYUP))
                {
                    _musicPlayerChordDown = false;
                }
                // Handle Global Media Keys (Galaxy Buds / Bluetooth Earbuds in-ear pause or button presses)
                if ((vkCode == 0xB3 || vkCode == 0xB2 || vkCode == 0xB0 || vkCode == 0xB1) &&
                    (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
                {
                    switch (vkCode)
                    {
                        case 0xB3: // VK_MEDIA_PLAY_PAUSE
                            LocalAudioPlayerService.Instance.TogglePlayPause();
                            break;
                        case 0xB2: // VK_MEDIA_STOP
                            LocalAudioPlayerService.Instance.Pause();
                            break;
                        case 0xB0: // VK_MEDIA_NEXT_TRACK
                            LocalAudioPlayerService.Instance.PlayNext();
                            break;
                        case 0xB1: // VK_MEDIA_PREV_TRACK
                            LocalAudioPlayerService.Instance.PlayPrevious();
                            break;
                    }
                }

                // Intercept Scroll Lock key (0x91) OR Ctrl+Alt+P (0x50) to toggle pause state
                bool isCtrlAltP = vkCode == 0x50 && IsControlPressed() && IsAltPressed();
                if ((vkCode == 0x91 || isCtrlAltP) && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
                {
                    IsPaused = !IsPaused;
                    if (IsPaused)
                    {
                        CancelRecording();
                        _rollingBuffer.Clear();
                    }
                    OnPauseToggled?.Invoke(IsPaused);

                    if (isCtrlAltP)
                    {
                        return (IntPtr)1; // Swallow the 'P' key so it doesn't print character 'p'
                    }
                }

                if (IsPaused)
                {
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // Auto-Expand checks when not recording
                if (!_isRecording)
                {
                    if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    {
                        if (vkCode == VK_BACK)
                        {
                            if (_rollingBuffer.Length > 0)
                                _rollingBuffer.Remove(_rollingBuffer.Length - 1, 1);
                        }
                        else if (vkCode == VK_ESCAPE || vkCode == VK_RETURN || vkCode == 0x25 || vkCode == 0x26 || vkCode == 0x27 || vkCode == 0x28 || vkCode == 0x09) // Escape, Enter, Arrows, Tab
                        {
                            _rollingBuffer.Clear();
                        }
                        else if (IsControlPressed() || IsAltPressed())
                        {
                            _rollingBuffer.Clear();
                        }
                        else if (vkCode != VK_LCONTROL && vkCode != VK_RCONTROL && vkCode != 0x10 && vkCode != 0xA0 && vkCode != 0xA1) // Ignore modifiers
                        {
                            char ch = GetCharFromKey(vkCode);
                            if (ch != '\0')
                            {
                                _rollingBuffer.Append(ch);
                                if (_rollingBuffer.Length > 30)
                                {
                                    _rollingBuffer.Remove(0, _rollingBuffer.Length - 30);
                                }

                                string currentTyped = _rollingBuffer.ToString();
                                string? matchedShortcut = AutoExpandShortcuts.FirstOrDefault(s => currentTyped.EndsWith(s, StringComparison.OrdinalIgnoreCase));
                                if (matchedShortcut != null)
                                {
                                    if (IsShortcutAllowed == null || IsShortcutAllowed(matchedShortcut))
                                    {
                                        _rollingBuffer.Clear();
                                        OnAutoExpandTriggered?.Invoke(matchedShortcut, currentTyped);
                                        return (IntPtr)1; // Swallow the trigger character keydown
                                    }
                                }
                            }
                        }
                    }
                }

                // Track Left Ctrl state
                if (vkCode == VK_LCONTROL)
                {
                    if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    {
                        if (!_lCtrlDown)
                        {
                            _lCtrlDown = true;
                            _lCtrlUsed = false;
                        }
                    }
                    else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        _lCtrlDown = false;
                        if (!_lCtrlUsed)
                        {
                            // Left Ctrl was tapped!
                            var now = DateTime.Now;
                            var elapsed = now - _lastLCtrlReleaseTime;
                            _lastLCtrlReleaseTime = now;

                            if (elapsed.TotalMilliseconds < 300)
                            {
                                _isRecording = false;
                                _buffer.Clear();
                                if (_commandPaletteGesture.IsDoubleLeftCtrl)
                                {
                                    OnDoubleTapLCtrl?.Invoke();
                                }
                                OnBufferChanged?.Invoke(string.Empty);
                            }
                            else
                            {
                                // Single tap -> Toggle recording mode
                                _isRecording = true;
                                _buffer.Clear();
                                OnBufferChanged?.Invoke(string.Empty);
                                SoundManager.PlayTick();
                            }
                        }
                    }
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                // If Left Ctrl is held down, track that it was used in combination
                if (_lCtrlDown && (message == WM_KEYDOWN || message == WM_SYSKEYDOWN))
                {
                    _lCtrlUsed = true;
                    // Standard shortcuts like Ctrl+C should cancel recording
                    CancelRecording();
                    return CallNextHookEx(_hookId, nCode, wParam, lParam);
                }

                if (_isRecording)
                {
                    if (message == WM_KEYDOWN || message == WM_SYSKEYDOWN)
                    {
                        // Right Ctrl -> Trigger Text Replacement
                        if (vkCode == VK_RCONTROL)
                        {
                            string keyword = _buffer.ToString();
                            _isRecording = false;
                            _buffer.Clear();
                            OnBufferChanged?.Invoke(string.Empty);
                            OnReplacementTriggered?.Invoke(keyword);
                            return (IntPtr)1; // Suppress Right Ctrl
                        }

                        // Right Shift -> Trigger App Action
                        if (vkCode == VK_RSHIFT)
                        {
                            string keyword = _buffer.ToString();
                            _isRecording = false;
                            _buffer.Clear();
                            OnBufferChanged?.Invoke(string.Empty);
                            OnActionTriggered?.Invoke(keyword);
                            return (IntPtr)1; // Suppress Right Shift
                        }

                        // Escape -> Cancel recording
                        if (vkCode == VK_ESCAPE)
                        {
                            CancelRecording();
                            return CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }

                        // Backspace -> Delete last char in buffer
                        if (vkCode == VK_BACK)
                        {
                            if (_buffer.Length > 0)
                            {
                                _buffer.Remove(_buffer.Length - 1, 1);
                                OnBufferChanged?.Invoke(_buffer.ToString());
                                SoundManager.PlayTick();
                            }
                            // If we suppress keys during recording, suppress backspace too
                            return SuppressKeysDuringRecording ? (IntPtr)1 : CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }

                        // Check if key is a character
                        char ch = GetCharFromKey(vkCode);
                        if (ch != '\0')
                        {
                            _buffer.Append(ch);
                            OnBufferChanged?.Invoke(_buffer.ToString());
                            SoundManager.PlayTick();
                            
                            // If SuppressKeysDuringRecording is true, swallow the keypress
                            return SuppressKeysDuringRecording ? (IntPtr)1 : CallNextHookEx(_hookId, nCode, wParam, lParam);
                        }
                        else
                        {
                            // Allow Shift keys to pass through without cancelling recording
                            if (vkCode == 0x10 || vkCode == 0xA0 || vkCode == 0xA1)
                            {
                                return CallNextHookEx(_hookId, nCode, wParam, lParam);
                            }

                            // Non-character key cancels recording
                            CancelRecording();
                        }
                    }
                    else if (message == WM_KEYUP || message == WM_SYSKEYUP)
                    {
                        // Suppress keyups of recorded characters if suppressing down events
                        char ch = GetCharFromKey(vkCode);
                        if (SuppressKeysDuringRecording && (ch != '\0' || vkCode == VK_BACK))
                        {
                            return (IntPtr)1;
                        }
                    }
                }
            }

            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        private bool TryHandleConfiguredCommandPaletteHotkey(
            int vkCode,
            int message)
        {
            if (IsCapturingCommandPaletteHotkey)
            {
                return false;
            }

            bool isKeyDown = message == WM_KEYDOWN || message == WM_SYSKEYDOWN;
            bool isKeyUp = message == WM_KEYUP || message == WM_SYSKEYUP;
            if (_commandPaletteGesture.IsDisabled ||
                vkCode != _commandPaletteGesture.VirtualKey)
            {
                return false;
            }

            if (isKeyDown && _commandPaletteGesture.ModifiersMatch())
            {
                if (!_commandPaletteChordDown)
                {
                    _commandPaletteChordDown = true;
                    _isRecording = false;
                    _buffer.Clear();
                    _rollingBuffer.Clear();
                    OnDoubleTapLCtrl?.Invoke();
                    OnBufferChanged?.Invoke(string.Empty);
                }

                return true;
            }

            if (isKeyUp && _commandPaletteChordDown)
            {
                _commandPaletteChordDown = false;
                return true;
            }

            return false;
        }

        private static bool IsShiftPressed()
        {
            return (GetKeyState(0x10) & 0x8000) != 0; // VK_SHIFT = 0x10
        }

        private char GetCharFromKey(int vkCode)
        {
            bool shift = IsShiftPressed();

            // Letters A-Z
            if (vkCode >= 0x41 && vkCode <= 0x5A)
            {
                return shift ? (char)vkCode : (char)(vkCode + 32);
            }

            // Standard digits 0-9
            if (vkCode >= 0x30 && vkCode <= 0x39)
            {
                if (shift)
                {
                    char[] shiftedNums = { ')', '!', '@', '#', '$', '%', '^', '&', '*', '(' };
                    return shiftedNums[vkCode - 0x30];
                }
                return (char)vkCode;
            }

            // Numpad digits 0-9
            if (vkCode >= 0x60 && vkCode <= 0x69)
            {
                return (char)('0' + (vkCode - 0x60));
            }

            // Numpad operators
            if (vkCode == 0x6A) return '*';
            if (vkCode == 0x6B) return '+';
            if (vkCode == 0x6D) return '-';
            if (vkCode == 0x6E) return '.';
            if (vkCode == 0x6F) return '/';

            // Space
            if (vkCode == 0x20) return ' ';

            // Punctuation & Symbols
            if (vkCode == 189) return shift ? '_' : '-';   // Hyphen/Underscore
            if (vkCode == 186) return shift ? ':' : ';';   // Semicolon/Colon
            if (vkCode == 187) return shift ? '+' : '=';   // Equal/Plus
            if (vkCode == 188) return shift ? '<' : ',';   // Comma/Less Than
            if (vkCode == 190) return shift ? '>' : '.';   // Period/Greater Than
            if (vkCode == 191) return shift ? '?' : '/';   // Slash/Question
            if (vkCode == 220) return shift ? '|' : '\\';  // Backslash/Pipe
            if (vkCode == 192) return shift ? '~' : '`';   // Backtick/Tilde
            if (vkCode == 219) return shift ? '{' : '[';   // Left Bracket/Brace
            if (vkCode == 221) return shift ? '}' : ']';   // Right Bracket/Brace
            if (vkCode == 222) return shift ? '"' : '\'';  // Quote/Double Quote

            return '\0';
        }

        public void Dispose()
        {
            Unhook();
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);
    }
}
