using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Threading.Tasks;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace KeyMapper
{
    public partial class MainWindow : Window
    {
        // Low-level Win32 imports for active window checking
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        private readonly KeyboardHook _hook;
        private readonly MouseHook _mouseHook;
        private readonly OverlayWindow _overlayWindow;
        private readonly PetOverlayWindow _petOverlayWindow;
        private readonly TrayIconManager _trayManager;
        private AppSettings _settings;
        private string _lastClipboardText = string.Empty;

        public static List<string> ClipboardHistory { get; } = new List<string>();
        public ObservableCollection<ShortcutMapping> Replacements { get; } = new ObservableCollection<ShortcutMapping>();
        public ObservableCollection<ShortcutMapping> Actions { get; } = new ObservableCollection<ShortcutMapping>();
        public ObservableCollection<string> Exclusions { get; } = new ObservableCollection<string>();

        private bool _isShuttingDown = false;
        private bool _isInitializing = true;

        public MainWindow()
        {
            InitializeComponent();

            // Load settings
            _settings = ConfigManager.Load();
            PopulateCollections();

            // Setup bindings
            ReplacementsList.ItemsSource = Replacements;
            ActionsList.ItemsSource = Actions;
            ExclusionsList.ItemsSource = Exclusions;

            // Setup subcomponents
            _hook = new KeyboardHook
            {
                IsEnabled = true,
                SuppressKeysDuringRecording = _settings.SuppressKeysDuringRecording,
                AutoExpandShortcuts = _settings.AutoExpandShortcuts ?? new List<string>()
            };

            _overlayWindow = new OverlayWindow();
            _petOverlayWindow = new PetOverlayWindow();
            if (_settings.ShowPetOverlay)
            {
                _petOverlayWindow.Show();
            }
            _trayManager = new TrayIconManager(this, _hook);

            _mouseHook = new MouseHook();

            // Bind Hook Events
            _hook.OnBufferChanged += Hook_OnBufferChanged;
            _hook.OnReplacementTriggered += Hook_OnReplacementTriggered;
            _hook.OnActionTriggered += Hook_OnActionTriggered;
            _hook.OnRecordingCancelled += Hook_OnRecordingCancelled;
            _hook.OnDoubleTapLCtrl += Hook_OnDoubleTapLCtrl;
            _hook.OnDeGibberishRequested += Hook_OnDeGibberishRequested;
            _hook.OnAutoExpandTriggered += Hook_OnAutoExpandTriggered;
            _hook.IsShortcutAllowed = IsShortcutAllowedByActiveWindow;
            _hook.OnPauseToggled += Hook_OnPauseToggled;

            _mouseHook.OnShiftRightClick += Hook_OnShiftRightClick;

            // Start monitor
            StartClipboardMonitor();

            // Start hook
            _hook.Hook();
            _mouseHook.Hook();

            // Initialize UI elements state
            SuppressKeysChk.IsChecked = _settings.SuppressKeysDuringRecording;
            ShowOverlayChk.IsChecked = _settings.ShowOverlay;
            RunAtStartupChk.IsChecked = _settings.RunAtStartup;
            PlaySoundsChk.IsChecked = _settings.PlaySounds;
            AiEndpointTxt.Text = _settings.AiApiEndpoint;
            AiApiKeyTxt.Password = _settings.AiApiKey;
            AiModelTxt.Text = _settings.AiModel;
            LocalAiEnabledChk.IsChecked = _settings.LocalAiEnabled;
            AiAmbientCommentsChk.IsChecked = _settings.AiAmbientCommentsEnabled;
            ThemePicker.SelectedValue = ThemeManager.Normalize(_settings.ThemeName);

            LocalAiModelOption recommended =
                LocalAiService.Instance.GetRecommendedModel();
            LocalAiRecommendationTxt.Text =
                $"Recommended for this PC: {recommended.DisplayName}";
            LocalAiHardwareTxt.Text =
                LocalAiService.Instance.GetHardwareSummary() +
                " · You can choose a smaller model at any time.";
            LocalAiModelPicker.SelectedValue =
                LocalAiService.Instance.FindModel(_settings.LocalAiModelId)?.Id ??
                recommended.Id;
            RefreshLocalAiModelUi();
            SoundManager.PlaySounds = _settings.PlaySounds;

            UpdateHookStatusUI();

            _isInitializing = false;
        }

        private void PopulateCollections()
        {
            Replacements.Clear();
            foreach (var r in _settings.Replacements)
            {
                Replacements.Add(new ShortcutMapping
                {
                    Shortcut = r.Shortcut,
                    Target = r.Target,
                    IsAutoExpand = r.IsAutoExpand,
                    AllowedProcess = r.AllowedProcess,
                    ExcludedProcess = r.ExcludedProcess,
                    UsageCount = r.UsageCount
                });
            }

            Actions.Clear();
            foreach (var a in _settings.Actions)
            {
                Actions.Add(new ShortcutMapping
                {
                    Shortcut = a.Shortcut,
                    Target = a.Target,
                    AllowedProcess = a.AllowedProcess,
                    ExcludedProcess = a.ExcludedProcess,
                    UsageCount = a.UsageCount
                });
            }

            Exclusions.Clear();
            foreach (var process in _settings.ExcludedProcesses)
            {
                Exclusions.Add(process.ToLower());
            }
        }

        private void SaveSettings()
        {
            if (_isInitializing) return;

            // Sync collection to Settings list of ShortcutConfig objects
            _settings.Replacements = Replacements.Select(x => new ShortcutConfig
            {
                Shortcut = x.Shortcut,
                Target = x.Target,
                IsAutoExpand = x.IsAutoExpand,
                AllowedProcess = x.AllowedProcess,
                ExcludedProcess = x.ExcludedProcess,
                UsageCount = x.UsageCount
            }).ToList();

            _settings.Actions = Actions.Select(x => new ShortcutConfig
            {
                Shortcut = x.Shortcut,
                Target = x.Target,
                AllowedProcess = x.AllowedProcess,
                ExcludedProcess = x.ExcludedProcess,
                UsageCount = x.UsageCount
            }).ToList();

            _settings.ExcludedProcesses = Exclusions.ToList();
            _settings.AutoExpandShortcuts = Replacements.Where(x => x.IsAutoExpand).Select(x => x.Shortcut).ToList();
            _settings.SuppressKeysDuringRecording = SuppressKeysChk.IsChecked ?? true;
            _settings.ShowOverlay = ShowOverlayChk.IsChecked ?? true;
            _settings.RunAtStartup = RunAtStartupChk.IsChecked ?? false;
            _settings.PlaySounds = PlaySoundsChk.IsChecked ?? true;
            _settings.ThemeName =
                ThemePicker.SelectedValue as string ?? "Warm Cream";
            _settings.LocalAiEnabled = LocalAiEnabledChk.IsChecked ?? true;
            _settings.AiAmbientCommentsEnabled =
                AiAmbientCommentsChk.IsChecked ?? true;
            _settings.LocalAiModelId =
                LocalAiModelPicker.SelectedValue as string ?? string.Empty;
            _settings.AiApiEndpoint = AiEndpointTxt.Text.Trim();
            _settings.AiApiKey = AiApiKeyTxt.Password.Trim();
            _settings.AiModel = string.IsNullOrWhiteSpace(AiModelTxt.Text)
                ? "gpt-4o-mini"
                : AiModelTxt.Text.Trim();

            ConfigManager.Save(_settings);

            SoundManager.PlaySounds = _settings.PlaySounds;

            // Update hook properties
            if (_hook != null)
            {
                _hook.SuppressKeysDuringRecording = _settings.SuppressKeysDuringRecording;
                _hook.AutoExpandShortcuts = _settings.AutoExpandShortcuts;
            }
        }

        private string GetActiveProcessName()
        {
            try
            {
                IntPtr hwnd = GetForegroundWindow();
                if (hwnd == IntPtr.Zero) return string.Empty;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == 0) return string.Empty;
                using (var proc = Process.GetProcessById((int)pid))
                {
                    return proc.ProcessName.ToLower() + ".exe";
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool IsActiveProcessExcluded()
        {
            string processName = GetActiveProcessName();
            if (string.IsNullOrEmpty(processName)) return false;
            return Exclusions.Contains(processName);
        }

        private void StartClipboardMonitor()
        {
            var timer = new System.Windows.Threading.DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string currentText = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(currentText) && currentText != _lastClipboardText)
                        {
                            _lastClipboardText = currentText;
                            AddToClipboardHistory(currentText);
                        }
                    }
                }
                catch { /* ignore clipboard locks */ }
            };
            timer.Start();
        }

        private void AddToClipboardHistory(string text)
        {
            ClipboardHistory.Remove(text);
            ClipboardHistory.Insert(0, text);
            if (ClipboardHistory.Count > 5)
            {
                ClipboardHistory.RemoveAt(ClipboardHistory.Count - 1);
            }
        }

        private void Hook_OnDoubleTapLCtrl()
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Bring existing palette to focus if already open
                    foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
                    {
                        if (window is CommandPaletteWindow)
                        {
                            window.Activate();
                            return;
                        }
                    }

                    var palette = new CommandPaletteWindow(this);
                    palette.Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open command palette: {ex.Message}");
                }
            }));
        }

        public void OpenCommandPalette()
        {
            Hook_OnDoubleTapLCtrl();
        }

        private void Hook_OnDeGibberishRequested(IntPtr targetWindow)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                // Let the user release Ctrl+Alt before the converter sends Ctrl+C;
                // otherwise Windows can interpret the copy as Ctrl+Alt+C.
                await Task.Delay(220);
                await _petOverlayWindow.DeGibberishSelectedTextAsync(targetWindow);
            }));
        }

        public void ExecuteReplacementDirectly(string keyword)
        {
            ExecuteReplacement(keyword, true);
        }

        private bool IsShortcutAllowedByActiveWindow(string keyword)
        {
            try
            {
                var mapping = Replacements.FirstOrDefault(x => x.Shortcut.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (mapping == null) return false;

                string activeProcess = GetActiveProcessName();
                if (string.IsNullOrEmpty(activeProcess)) return false;

                // 1. Allowed App Check
                if (!string.IsNullOrEmpty(mapping.AllowedProcess))
                {
                    string allowed = mapping.AllowedProcess.Trim().ToLower();
                    if (!allowed.EndsWith(".exe")) allowed += ".exe";
                    if (!activeProcess.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return false;
                }

                // 2. Excluded App Check
                if (!string.IsNullOrEmpty(mapping.ExcludedProcess))
                {
                    string excluded = mapping.ExcludedProcess.Trim().ToLower();
                    if (!excluded.EndsWith(".exe")) excluded += ".exe";
                    if (activeProcess.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Hook_OnAutoExpandTriggered(string keyword)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (IsActiveProcessExcluded()) return;

                var mapping = Replacements.FirstOrDefault(x => x.Shortcut.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (mapping != null)
                {
                    string active = GetActiveProcessName();

                    // Check if AllowedProcess is restricted
                    if (!string.IsNullOrEmpty(mapping.AllowedProcess))
                    {
                        string allowed = mapping.AllowedProcess.Trim().ToLower();
                        if (!allowed.EndsWith(".exe")) allowed += ".exe";
                        if (!active.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return;
                    }

                    // Check if ExcludedProcess is restricted
                    if (!string.IsNullOrEmpty(mapping.ExcludedProcess))
                    {
                        string excluded = mapping.ExcludedProcess.Trim().ToLower();
                        if (!excluded.EndsWith(".exe")) excluded += ".exe";
                        if (active.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return;
                    }

                    _hook.IsEnabled = false;

                    // Increment usage count
                    mapping.UsageCount++;
                    SaveSettings();

                    string targetText = mapping.Target;

                    // Match casing style
                    targetText = MatchCaseStyle(keyword, targetText);

                    // Evaluate offset dates and standard macros
                    targetText = ProcessDateArithmetic(targetText);
                    targetText = targetText.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
                    targetText = targetText.Replace("{time}", DateTime.Now.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);

                    if (targetText.Contains("{clip}", StringComparison.OrdinalIgnoreCase))
                    {
                        string clipText = GetClipboardTextWithRetry();
                        targetText = targetText.Replace("{clip}", clipText, StringComparison.OrdinalIgnoreCase);
                    }

                    int leftArrows = 0;
                    if (targetText.Contains("{cursor}", StringComparison.OrdinalIgnoreCase))
                    {
                        int cursorIndex = targetText.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase);
                        targetText = targetText.Replace("{cursor}", string.Empty, StringComparison.OrdinalIgnoreCase);
                        leftArrows = targetText.Length - cursorIndex;
                    }

                    // For Auto-expand, we swallow the last trigger char, so we backspace keyword.Length - 1
                    int backspaces = keyword.Length - 1;
                    InputSimulator.SimulateReplacement(backspaces, targetText, leftArrows);
                    SoundManager.PlaySuccess();

                    _hook.IsEnabled = true;
                }
            }));
        }

        private string ProcessDateArithmetic(string text)
        {
            try
            {
                var matches = System.Text.RegularExpressions.Regex.Matches(text, @"\{date([+-]\d+)([dmh])\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                foreach (System.Text.RegularExpressions.Match match in matches)
                {
                    if (match.Success)
                    {
                        string offsetStr = match.Groups[1].Value;
                        string unit = match.Groups[2].Value.ToLower();
                        if (int.TryParse(offsetStr, out int offset))
                        {
                            DateTime dt = DateTime.Now;
                            if (unit == "d") dt = dt.AddDays(offset);
                            else if (unit == "m") dt = dt.AddMonths(offset);
                            else if (unit == "h") dt = dt.AddHours(offset);

                            text = text.Replace(match.Value, dt.ToString("yyyy-MM-dd"));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Date arithmetic error: {ex.Message}");
            }
            return text;
        }

        private static string MatchCaseStyle(string keyword, string targetText)
        {
            if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(targetText)) return targetText;

            // 1. Check if all alphabetic characters in keyword are uppercase
            bool isAllUpper = keyword.Any(char.IsLetter) && keyword.Where(char.IsLetter).All(char.IsUpper);
            if (isAllUpper)
            {
                return targetText.ToUpper();
            }

            // 2. Check if the first letter of keyword is uppercase
            char firstLetter = keyword.FirstOrDefault(char.IsLetter);
            if (firstLetter != '\0' && char.IsUpper(firstLetter))
            {
                return char.ToUpper(targetText[0]) + targetText.Substring(1);
            }

            return targetText;
        }

        private static string EvaluateMathExpression(string expression)
        {
            try
            {
                using (var dt = new DataTable())
                {
                    var result = dt.Compute(expression, "");
                    return result.ToString() ?? "0";
                }
            }
            catch (Exception ex)
            {
                return $"[Error: {ex.Message}]";
            }
        }

        private static string GetClipboardTextWithRetry()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        return Clipboard.GetText();
                    }
                    return string.Empty;
                }
                catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0) // CLIPBRD_E_CANT_OPEN
                {
                    System.Threading.Thread.Sleep(20);
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
            return string.Empty;
        }

        private static void SetClipboardTextWithRetry(string text)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return;
                }
                catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0) // CLIPBRD_E_CANT_OPEN
                {
                    System.Threading.Thread.Sleep(20);
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        private static void ClearClipboardWithRetry()
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.Clear();
                    return;
                }
                catch (COMException ex) when ((uint)ex.ErrorCode == 0x800401D0) // CLIPBRD_E_CANT_OPEN
                {
                    System.Threading.Thread.Sleep(20);
                }
                catch
                {
                    System.Threading.Thread.Sleep(20);
                }
            }
        }

        public void ExecuteActionDirectly(string keyword)
        {
            Hook_OnActionTriggered(keyword);
        }

        #region Hook Event Handlers

        private void Hook_OnBufferChanged(string buffer)
        {
            Dispatcher.Invoke(() =>
            {
                if (_settings.ShowOverlay && !string.IsNullOrEmpty(buffer))
                {
                    _overlayWindow.ShowBuffer(buffer);
                }
                else
                {
                    _overlayWindow.HideBuffer();
                }
            });
        }

        private void Hook_OnRecordingCancelled()
        {
            Dispatcher.Invoke(() =>
            {
                _overlayWindow.HideBuffer();
            });
        }

        private void Hook_OnReplacementTriggered(string keyword)
        {
            ExecuteReplacement(keyword, false);
        }

        private void Hook_OnAutoExpandTriggered(string keyword, string fullBuffer)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                if (IsActiveProcessExcluded()) return;

                var mapping = Replacements.FirstOrDefault(x => x.Shortcut.Equals(keyword, StringComparison.OrdinalIgnoreCase));
                if (mapping != null)
                {
                    string active = GetActiveProcessName();

                    // Check if AllowedProcess is restricted
                    if (!string.IsNullOrEmpty(mapping.AllowedProcess))
                    {
                        string allowed = mapping.AllowedProcess.Trim().ToLower();
                        if (!allowed.EndsWith(".exe")) allowed += ".exe";
                        if (!active.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return;
                    }

                    // Check if ExcludedProcess is restricted
                    if (!string.IsNullOrEmpty(mapping.ExcludedProcess))
                    {
                        string excluded = mapping.ExcludedProcess.Trim().ToLower();
                        if (!excluded.EndsWith(".exe")) excluded += ".exe";
                        if (active.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return;
                    }

                    int backspaces = keyword.Length - 1;
                    await ProcessAndPerformReplacement(mapping, keyword, backspaces, fullBuffer);
                }
            }));
        }

        private async void ExecuteReplacement(string keyword, bool isDirect)
        {
            if (IsActiveProcessExcluded()) return;

            // Check dynamic calculator macro (starts with "calc:")
            if (keyword.StartsWith("calc:", StringComparison.OrdinalIgnoreCase))
            {
                string expression = keyword.Substring(5).Trim();
                string result = EvaluateMathExpression(expression);
                int backspaces = isDirect ? 0 : (_settings.SuppressKeysDuringRecording ? 0 : keyword.Length);
                InputSimulator.SimulateReplacement(backspaces, result, 0);
                SoundManager.PlaySuccess();
                return;
            }

            // Check built-in clipboard history macros (c1 - c5)
            if (keyword.Length == 2 && keyword[0] == 'c' && char.IsDigit(keyword[1]))
            {
                int index = (keyword[1] - '0') - 1;
                if (index >= 0 && index < ClipboardHistory.Count)
                {
                    _hook.IsEnabled = false;
                    string targetText = ClipboardHistory[index];
                    int backspaces = isDirect ? 0 : (_settings.SuppressKeysDuringRecording ? 0 : keyword.Length);
                    InputSimulator.SimulateReplacement(backspaces, targetText, 0);
                    SoundManager.PlaySuccess();
                    _hook.IsEnabled = true;
                }
                return;
            }

            // Check case converters (up, low, cap)
            if (keyword.Equals("up", StringComparison.OrdinalIgnoreCase) || 
                keyword.Equals("low", StringComparison.OrdinalIgnoreCase) || 
                keyword.Equals("cap", StringComparison.OrdinalIgnoreCase))
            {
                string clipText = GetClipboardTextWithRetry();
                if (!string.IsNullOrEmpty(clipText))
                {
                    _hook.IsEnabled = false;
                    string transformedText = clipText;

                    if (keyword.Equals("up", StringComparison.OrdinalIgnoreCase))
                        transformedText = clipText.ToUpper();
                    else if (keyword.Equals("low", StringComparison.OrdinalIgnoreCase))
                        transformedText = clipText.ToLower();
                    else if (keyword.Equals("cap", StringComparison.OrdinalIgnoreCase))
                        transformedText = System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(clipText.ToLower());

                    int backspaces = isDirect ? 0 : (_settings.SuppressKeysDuringRecording ? 0 : keyword.Length);
                    InputSimulator.SimulateReplacement(backspaces, transformedText, 0);
                    SoundManager.PlaySuccess();
                    _hook.IsEnabled = true;
                }
                return;
            }

            // Find user-defined replacement value
            var mapping = Replacements.FirstOrDefault(x => x.Shortcut.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                string active = GetActiveProcessName();

                // Check allowed process
                if (!string.IsNullOrEmpty(mapping.AllowedProcess))
                {
                    string allowed = mapping.AllowedProcess.Trim().ToLower();
                    if (!allowed.EndsWith(".exe")) allowed += ".exe";
                    if (!active.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return;
                }

                // Check excluded process
                if (!string.IsNullOrEmpty(mapping.ExcludedProcess))
                {
                    string excluded = mapping.ExcludedProcess.Trim().ToLower();
                    if (!excluded.EndsWith(".exe")) excluded += ".exe";
                    if (active.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return;
                }

                int backspaces = isDirect 
                    ? 0 
                    : (_settings.SuppressKeysDuringRecording ? 0 : keyword.Length);

                await ProcessAndPerformReplacement(mapping, keyword, backspaces, null);
            }
        }

        private async Task ProcessAndPerformReplacement(ShortcutMapping mapping, string keyword, int backspaces, string? fullBuffer = null)
        {
            _hook.IsEnabled = false;

            // Increment usage
            mapping.UsageCount++;
            SaveSettings();

            string targetText = mapping.Target;

            // Check if it is a Text Function (starts with "fn:")
            if (targetText.StartsWith("fn:", StringComparison.OrdinalIgnoreCase))
            {
                string fnName = targetText.Substring(3).Trim();
                string input = "";

                // Get input context
                if (fullBuffer != null)
                {
                    // Auto-Expand context: take the typed buffer before the keyword
                    if (fullBuffer.Length > keyword.Length)
                    {
                        input = fullBuffer.Substring(0, fullBuffer.Length - keyword.Length);
                    }
                    // The backspaces must also delete the input prefix!
                    backspaces += input.Length;
                }
                else
                {
                    // Standard context: grab selection
                    input = await GrabSelectionAsync();
                }

                targetText = ExecuteTextFunction(fnName, input);
            }
            else
            {
                // Normal expansion processing
                targetText = MatchCaseStyle(keyword, targetText);
                targetText = ProcessDateArithmetic(targetText);
                targetText = targetText.Replace("{date}", DateTime.Now.ToString("yyyy-MM-dd"), StringComparison.OrdinalIgnoreCase);
                targetText = targetText.Replace("{time}", DateTime.Now.ToString("HH:mm"), StringComparison.OrdinalIgnoreCase);

                if (targetText.Contains("{clip}", StringComparison.OrdinalIgnoreCase))
                {
                    string clipText = GetClipboardTextWithRetry();
                    targetText = targetText.Replace("{clip}", clipText, StringComparison.OrdinalIgnoreCase);
                }
            }

            // Process {cursor} positioning
            int leftArrows = 0;
            if (targetText.Contains("{cursor}", StringComparison.OrdinalIgnoreCase))
            {
                int cursorIndex = targetText.IndexOf("{cursor}", StringComparison.OrdinalIgnoreCase);
                targetText = targetText.Replace("{cursor}", string.Empty, StringComparison.OrdinalIgnoreCase);
                leftArrows = targetText.Length - cursorIndex;
            }

            // Perform typing or pasting
            if (targetText.Contains('\n') || targetText.Contains('\r'))
            {
                if (backspaces > 0)
                {
                    InputSimulator.SimulateReplacement(backspaces, string.Empty, 0);
                }
                await PasteTextViaClipboardAsync(targetText);
                if (leftArrows > 0)
                {
                    InputSimulator.SimulateReplacement(0, string.Empty, leftArrows);
                }
            }
            else
            {
                InputSimulator.SimulateReplacement(backspaces, targetText, leftArrows);
            }

            SoundManager.PlaySuccess();
            _hook.IsEnabled = true;
        }

        private async Task<string> GrabSelectionAsync()
        {
            string originalText = "";
            bool hadText = false;
            try
            {
                if (Clipboard.ContainsText())
                {
                    originalText = Clipboard.GetText();
                    hadText = true;
                }
            }
            catch { }

            try
            {
                Clipboard.Clear();
            }
            catch { }

            InputSimulator.SimulateCopy();
            await Task.Delay(100);

            string selection = "";
            try
            {
                selection = Clipboard.GetText();
            }
            catch { }

            if (hadText)
            {
                try
                {
                    Clipboard.SetText(originalText);
                }
                catch { }
            }

            return selection;
        }

        private async Task PasteTextViaClipboardAsync(string text)
        {
            string originalText = "";
            bool hadText = false;
            try
            {
                if (Clipboard.ContainsText())
                {
                    originalText = Clipboard.GetText();
                    hadText = true;
                }
            }
            catch { }

            bool success = false;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    success = true;
                    break;
                }
                catch
                {
                    await Task.Delay(20);
                }
            }

            if (success)
            {
                InputSimulator.SimulatePaste();
                await Task.Delay(80);
            }

            if (hadText)
            {
                for (int i = 0; i < 5; i++)
                {
                    try
                    {
                        Clipboard.SetText(originalText);
                        break;
                    }
                    catch
                    {
                        await Task.Delay(20);
                    }
                }
            }
            else
            {
                try
                {
                    Clipboard.Clear();
                }
                catch { }
            }
        }

        private static readonly Dictionary<char, string> MorseAlphabet = new Dictionary<char, string>
        {
            {'a', ".-"}, {'b', "-..."}, {'c', "-.-."}, {'d', "-.."}, {'e', "."},
            {'f', "..-."}, {'g', "--."}, {'h', "...."}, {'i', ".."}, {'j', ".---"},
            {'k', "-.-"}, {'l', ".-.."}, {'m', "--"}, {'n', "-."}, {'o', "---"},
            {'p', ".--."}, {'q', "--.-"}, {'r', ".-."}, {'s', "..."}, {'t', "-"},
            {'u', "..-"}, {'v', "...-"}, {'w', ".--"}, {'x', "-..-"}, {'y', "-.--"},
            {'z', "--.."},
            {'0', "-----"}, {'1', ".----"}, {'2', "..---"}, {'3', "...--"}, {'4', "....-"},
            {'5', "....."}, {'6', "-...."}, {'7', "--..."}, {'8', "---.."}, {'9', "----."},
            {' ', "/"}
        };

        private string ToMorseCode(string text)
        {
            var result = new List<string>();
            foreach (char c in text.ToLower())
            {
                if (MorseAlphabet.TryGetValue(c, out string? morse))
                {
                    result.Add(morse);
                }
            }
            return string.Join(" ", result);
        }

        private string NormalizeMorseString(string morse)
        {
            if (string.IsNullOrEmpty(morse)) return string.Empty;

            // Normalize dots
            morse = morse.Replace('·', '.');
            morse = morse.Replace('•', '.');
            morse = morse.Replace('․', '.');
            morse = morse.Replace('٫', '.');

            // Normalize dashes
            morse = morse.Replace("—", "---"); // Em Dash
            morse = morse.Replace("–", "--");  // En Dash
            morse = morse.Replace('−', '-');   // Minus Sign
            morse = morse.Replace('―', '-');   // Horizontal Bar

            return morse;
        }

        private string FromMorseCode(string morse)
        {
            morse = NormalizeMorseString(morse);
            var reverseDict = MorseAlphabet.ToDictionary(x => x.Value, x => x.Key);
            var result = new System.Text.StringBuilder();
            
            string[] words = morse.Split(new[] { " / ", "/" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                string[] letters = word.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var letter in letters)
                {
                    if (reverseDict.TryGetValue(letter, out char c))
                    {
                        result.Append(c);
                    }
                }
                result.Append(' ');
            }
            return result.ToString().Trim();
        }

        private void LogDebug(string message)
        {
            try
            {
                string logPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debug_log.txt");
                System.IO.File.AppendAllText(logPath, "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "] " + message + Environment.NewLine);
            }
            catch { }
        }

        private string ExecuteTextFunction(string functionName, string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            switch (functionName.ToLower().Trim())
            {
                case "calc":
                    return EvaluateMathExpression(input);
                case "upper":
                    return input.ToUpper();
                case "lower":
                    return input.ToLower();
                case "title":
                    return System.Globalization.CultureInfo.CurrentCulture.TextInfo.ToTitleCase(input.ToLower());
                case "reverse":
                case "rev":
                    char[] arr = input.ToCharArray();
                    Array.Reverse(arr);
                    return new string(arr);
                case "len":
                    return input.Length.ToString();
                case "morse":
                case "mc":
                    string morseResult = ToMorseCode(input);
                    LogDebug("morse - Input: '" + input + "' (Len: " + input.Length + "), Output: '" + morseResult + "' (Len: " + morseResult.Length + ")");
                    return morseResult;
                case "unmorse":
                case "demorse":
                case "umc":
                    string unmorseResult = FromMorseCode(input);
                    LogDebug("unmorse - Input: '" + input + "' (Len: " + input.Length + "), Output: '" + unmorseResult + "' (Len: " + unmorseResult.Length + ")");
                    return unmorseResult;
                default:
                    return input;
            }
        }

        private void Hook_OnActionTriggered(string keyword)
        {
            if (IsActiveProcessExcluded()) return;

            // Try running built-in Windows System Action first
            if (SystemActions.TryExecute(keyword, out string statusMessage))
            {
                if (!string.IsNullOrEmpty(statusMessage))
                {
                    Dispatcher.Invoke(() =>
                    {
                        _trayManager.ShowNotification("System Action", statusMessage, System.Windows.Forms.ToolTipIcon.Info);
                    });
                }
                SoundManager.PlaySuccess();
                return;
            }

            var mapping = Actions.FirstOrDefault(x => x.Shortcut.Equals(keyword, StringComparison.OrdinalIgnoreCase));
            if (mapping != null)
            {
                string active = GetActiveProcessName();

                // Check allowed process
                if (!string.IsNullOrEmpty(mapping.AllowedProcess))
                {
                    string allowed = mapping.AllowedProcess.Trim().ToLower();
                    if (!allowed.EndsWith(".exe")) allowed += ".exe";
                    if (!active.Equals(allowed, StringComparison.OrdinalIgnoreCase)) return;
                }

                // Check excluded process
                if (!string.IsNullOrEmpty(mapping.ExcludedProcess))
                {
                    string excluded = mapping.ExcludedProcess.Trim().ToLower();
                    if (!excluded.EndsWith(".exe")) excluded += ".exe";
                    if (active.Equals(excluded, StringComparison.OrdinalIgnoreCase)) return;
                }

                // Execute action asynchronously to prevent blocking the low-level hook thread
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        // Increment usage
                        mapping.UsageCount++;
                        SaveSettings();

                        string target = mapping.Target.Trim();

                        // Check context-aware selection copy macro
                        if (target.Contains("{sel}", StringComparison.OrdinalIgnoreCase))
                        {
                            _hook.IsEnabled = false;

                            // 1. Backup clipboard text
                            string originalClipboard = GetClipboardTextWithRetry();
                            bool hadText = !string.IsNullOrEmpty(originalClipboard);

                            // 2. Clear clipboard and simulate Copy
                            ClearClipboardWithRetry();
                            InputSimulator.SimulateCopy();

                            // 3. Wait for the copy to register asynchronously (no UI blocking!)
                            await Task.Delay(80);

                            // 4. Retrieve copied selection
                            string selection = GetClipboardTextWithRetry();

                            // 5. Restore original clipboard
                            if (hadText)
                            {
                                SetClipboardTextWithRetry(originalClipboard);
                            }
                            else
                            {
                                ClearClipboardWithRetry();
                            }

                            _hook.IsEnabled = true;

                            // 6. Encode and replace tag in URL
                            string encodedSel = Uri.EscapeDataString(selection);
                            target = target.Replace("{sel}", encodedSel, StringComparison.OrdinalIgnoreCase);
                        }

                        ProcessStartInfo psi;

                        if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                            target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        {
                            // Launch Web URL
                            psi = new ProcessStartInfo
                            {
                                FileName = target,
                                UseShellExecute = true
                            };
                        }
                        else if (target.StartsWith("cmd:", StringComparison.OrdinalIgnoreCase))
                        {
                            // Run CMD Command silently
                            string command = target.Substring(4).Trim();
                            psi = new ProcessStartInfo
                            {
                                FileName = "cmd.exe",
                                Arguments = $"/c {command}",
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };
                        }
                        else if (target.StartsWith("powershell:", StringComparison.OrdinalIgnoreCase))
                        {
                            // Run PowerShell Command silently
                            string command = target.Substring(11).Trim();
                            psi = new ProcessStartInfo
                            {
                                FileName = "powershell.exe",
                                Arguments = $"-NoProfile -WindowStyle Hidden -Command \"{command}\"",
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden,
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                RedirectStandardError = true
                            };
                        }
                        else
                        {
                            // Launch normal process
                            psi = new ProcessStartInfo
                            {
                                FileName = target,
                                UseShellExecute = true
                            };
                        }

                        var process = Process.Start(psi);
                        if (process != null && (psi.RedirectStandardOutput || psi.RedirectStandardError))
                        {
                            string targetAction = target;
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var outputBuilder = new System.Text.StringBuilder();
                                    var errorBuilder = new System.Text.StringBuilder();

                                    process.OutputDataReceived += (sender, args) =>
                                    {
                                        if (args.Data != null) outputBuilder.AppendLine(args.Data);
                                    };
                                    process.ErrorDataReceived += (sender, args) =>
                                    {
                                        if (args.Data != null) errorBuilder.AppendLine(args.Data);
                                    };

                                    process.BeginOutputReadLine();
                                    process.BeginErrorReadLine();

                                    await process.WaitForExitAsync();

                                    string output = outputBuilder.ToString().Trim();
                                    string error = errorBuilder.ToString().Trim();

                                    if (!string.IsNullOrEmpty(output) || !string.IsNullOrEmpty(error))
                                    {
                                        string folderPath = System.IO.Path.Combine(
                                            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                            "KeyMapper"
                                        );
                                        System.IO.Directory.CreateDirectory(folderPath);
                                        string logPath = System.IO.Path.Combine(folderPath, "actions_log.txt");

                                        var sb = new System.Text.StringBuilder();
                                        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] Action: {targetAction}");
                                        if (!string.IsNullOrEmpty(output))
                                        {
                                            sb.AppendLine("--- STDOUT ---");
                                            sb.AppendLine(output);
                                        }
                                        if (!string.IsNullOrEmpty(error))
                                        {
                                            sb.AppendLine("--- STDERR ---");
                                            sb.AppendLine(error);
                                        }
                                        sb.AppendLine(new string('=', 40));
                                        await System.IO.File.AppendAllTextAsync(logPath, sb.ToString());
                                    }
                                }
                                catch { }
                            });
                        }

                        // Play success sound
                        SoundManager.PlaySuccess();
                    }
                    catch (Exception ex)
                    {
                        _trayManager.ShowNotification(
                            "Action Failed", 
                            $"Could not launch '{mapping.Target}': {ex.Message}", 
                            System.Windows.Forms.ToolTipIcon.Error
                        );
                    }
                }));
            }
        }

        #endregion

        #region UI Event Handlers

        public void UpdateHookStatusUI()
        {
            if (!_hook.IsEnabled)
            {
                StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 241, 234));
                StatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(200, 185, 145));
                StatusText.Text = "KeyMapper is off";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 123, 143));
                ToggleHookBtn.Content = "Turn on KeyMapper";
            }
            else if (_hook.IsPaused)
            {
                StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 240, 201));
                StatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(214, 169, 75));
                StatusText.Text = "KeyMapper is paused";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(138, 100, 26));
                ToggleHookBtn.Content = "Turn off KeyMapper";
            }
            else
            {
                StatusBadge.Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(225, 244, 234));
                StatusBadge.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(83, 167, 125));
                StatusText.Text = "KeyMapper is on";
                StatusText.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(36, 101, 72));
                ToggleHookBtn.Content = "Turn off KeyMapper";
            }
        }

        private void ToggleHook_Click(object sender, RoutedEventArgs e)
        {
            _hook.IsEnabled = !_hook.IsEnabled;
            _trayManager.UpdateIconState(_hook.IsEnabled);
            UpdateHookStatusUI();
        }

        private void AddReplacement_Click(object sender, RoutedEventArgs e)
        {
            string shortcut = AddRepShortcutTxt.Text.Trim().ToLower();
            string target = AddRepTargetTxt.Text.Trim();
            string allowedApp = AddRepAllowedProcessTxt.Text.Trim().ToLower();
            string excludedApp = AddRepExcludedProcessTxt.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(shortcut) || string.IsNullOrEmpty(target))
            {
                MessageBox.Show("Both abbreviation and replacement text must be specified.", "Invalid Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Replacements.Any(x => x.Shortcut == shortcut))
            {
                MessageBox.Show($"The abbreviation '{shortcut}' already exists.", "Duplicate Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (allowedApp.EndsWith(".exe")) allowedApp = allowedApp.Substring(0, allowedApp.Length - 4);
            if (excludedApp.EndsWith(".exe")) excludedApp = excludedApp.Substring(0, excludedApp.Length - 4);

            Replacements.Add(new ShortcutMapping { Shortcut = shortcut, Target = target, AllowedProcess = allowedApp, ExcludedProcess = excludedApp });
            SaveSettings();

            AddRepShortcutTxt.Clear();
            AddRepTargetTxt.Clear();
            AddRepAllowedProcessTxt.Clear();
            AddRepExcludedProcessTxt.Clear();
        }

        private void DeleteReplacement_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var item = (ShortcutMapping)btn.DataContext;
            Replacements.Remove(item);
            SaveSettings();
        }

        private void AddAction_Click(object sender, RoutedEventArgs e)
        {
            string shortcut = AddActShortcutTxt.Text.Trim().ToLower();
            string target = AddActTargetTxt.Text.Trim();
            string allowedApp = AddActAllowedProcessTxt.Text.Trim().ToLower();
            string excludedApp = AddActExcludedProcessTxt.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(shortcut) || string.IsNullOrEmpty(target))
            {
                MessageBox.Show("Both abbreviation and application path must be specified.", "Invalid Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (Actions.Any(x => x.Shortcut == shortcut))
            {
                MessageBox.Show($"The action '{shortcut}' already exists.", "Duplicate Key", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (allowedApp.EndsWith(".exe")) allowedApp = allowedApp.Substring(0, allowedApp.Length - 4);
            if (excludedApp.EndsWith(".exe")) excludedApp = excludedApp.Substring(0, excludedApp.Length - 4);

            Actions.Add(new ShortcutMapping { Shortcut = shortcut, Target = target, AllowedProcess = allowedApp, ExcludedProcess = excludedApp });
            SaveSettings();

            AddActShortcutTxt.Clear();
            AddActTargetTxt.Clear();
            AddActAllowedProcessTxt.Clear();
            AddActExcludedProcessTxt.Clear();
        }

        private void DeleteAction_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var item = (ShortcutMapping)btn.DataContext;
            Actions.Remove(item);
            SaveSettings();
        }

        private void BrowseApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*",
                Title = "Select Application to Launch"
            };

            if (dialog.ShowDialog() == true)
            {
                AddActTargetTxt.Text = dialog.FileName;
            }
        }

        private void PickActTargetApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                AddActTargetTxt.Text = ResolveProcessExecutablePath(proc);
            }
        }

        private string ResolveProcessExecutablePath(string processNameWithExe)
        {
            try
            {
                string nameOnly = processNameWithExe;
                if (nameOnly.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    nameOnly = nameOnly.Substring(0, nameOnly.Length - 4);
                }

                var procs = System.Diagnostics.Process.GetProcessesByName(nameOnly);
                if (procs.Length > 0)
                {
                    try
                    {
                        string? path = procs[0].MainModule?.FileName;
                        if (!string.IsNullOrEmpty(path) && File.Exists(path))
                        {
                            return path;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return processNameWithExe;
        }

        private void PickAllowedRepApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    proc = proc.Substring(0, proc.Length - 4);
                }
                AddRepAllowedProcessTxt.Text = proc;
            }
        }

        private void BrowseAllowedRepApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*", Title = "Select Application" };
            if (dialog.ShowDialog() == true)
            {
                AddRepAllowedProcessTxt.Text = Path.GetFileNameWithoutExtension(dialog.FileName).ToLower();
            }
        }

        private void PickExcludedRepApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    proc = proc.Substring(0, proc.Length - 4);
                }
                AddRepExcludedProcessTxt.Text = proc;
            }
        }

        private void BrowseExcludedRepApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*", Title = "Select Application" };
            if (dialog.ShowDialog() == true)
            {
                AddRepExcludedProcessTxt.Text = Path.GetFileNameWithoutExtension(dialog.FileName).ToLower();
            }
        }

        private void PickAllowedActApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    proc = proc.Substring(0, proc.Length - 4);
                }
                AddActAllowedProcessTxt.Text = proc;
            }
        }

        private void BrowseAllowedActApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*", Title = "Select Application" };
            if (dialog.ShowDialog() == true)
            {
                AddActAllowedProcessTxt.Text = Path.GetFileNameWithoutExtension(dialog.FileName).ToLower();
            }
        }

        private void PickExcludedActApp_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true && picker.SelectedProcesses.Count > 0)
            {
                string proc = picker.SelectedProcesses[0];
                if (proc.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    proc = proc.Substring(0, proc.Length - 4);
                }
                AddActExcludedProcessTxt.Text = proc;
            }
        }

        private void BrowseExcludedActApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog { Filter = "Executables (*.exe)|*.exe|All Files (*.*)|*.*", Title = "Select Application" };
            if (dialog.ShowDialog() == true)
            {
                AddActExcludedProcessTxt.Text = Path.GetFileNameWithoutExtension(dialog.FileName).ToLower();
            }
        }

        private void SettingChanged(object sender, RoutedEventArgs e)
        {
            SaveSettings();
        }

        private void ThemePicker_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            string themeName =
                ThemePicker.SelectedValue as string ?? "Warm Cream";
            ThemeManager.Apply(themeName);
            SaveSettings();
        }

        private void LocalAiModelPicker_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            RefreshLocalAiModelUi();
            SaveSettings();
        }

        private void RefreshLocalAiModelUi(string? status = null)
        {
            if (LocalAiModelPicker.SelectedItem is not LocalAiModelOption model)
            {
                LocalAiDescriptionTxt.Text = string.Empty;
                LocalAiStatusTxt.Text = "Choose a model to see its requirements.";
                LocalAiDownloadBtn.IsEnabled = false;
                LocalAiRemoveBtn.IsEnabled = false;
                return;
            }

            bool installed = LocalAiService.Instance.IsInstalled(model.Id);
            LocalAiDescriptionTxt.Text =
                $"{model.Description} Download: {FormatBytes(model.DownloadBytes)} · " +
                $"suggested memory: {model.SuggestedRamGb} GB or more.";
            LocalAiDownloadBtn.IsEnabled = !installed;
            LocalAiDownloadBtn.Content =
                installed ? "Downloaded" : "Download model";
            LocalAiRemoveBtn.IsEnabled = installed;
            LocalAiStatusTxt.Text = status ??
                (installed
                    ? "Ready · conversations and optional ambient comments can run offline."
                    : "Not downloaded · the pet continues using its built-in replies.");
        }

        private async void LocalAiDownload_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (LocalAiModelPicker.SelectedItem is not LocalAiModelOption model)
            {
                return;
            }

            LocalAiDownloadBtn.IsEnabled = false;
            LocalAiRemoveBtn.IsEnabled = false;
            LocalAiModelPicker.IsEnabled = false;
            LocalAiProgress.Value = 0;
            LocalAiProgress.Visibility = Visibility.Visible;
            LocalAiStatusTxt.Text =
                $"Downloading {model.DisplayName}… You can continue using the app.";

            var progress = new Progress<LocalAiDownloadProgress>(value =>
            {
                LocalAiProgress.Value = value.Percentage;
                LocalAiStatusTxt.Text =
                    $"Downloading {model.DisplayName} · {value.Percentage}% " +
                    $"({FormatBytes(value.BytesReceived)} of {FormatBytes(value.TotalBytes)})";
            });

            try
            {
                await LocalAiService.Instance.DownloadModelAsync(model, progress);
                _settings.LocalAiModelId = model.Id;
                _settings.LocalAiEnabled = true;
                LocalAiEnabledChk.IsChecked = true;
                ConfigManager.Save(_settings);
                RefreshLocalAiModelUi("Ready · model downloaded and enabled.");
            }
            catch (Exception ex)
            {
                RefreshLocalAiModelUi(
                    $"Download paused safely: {ex.Message}. Press Download to resume.");
            }
            finally
            {
                LocalAiProgress.Visibility = Visibility.Collapsed;
                LocalAiModelPicker.IsEnabled = true;
                RefreshLocalAiModelUi(LocalAiStatusTxt.Text);
            }
        }

        private async void LocalAiRemove_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (LocalAiModelPicker.SelectedItem is not LocalAiModelOption model)
            {
                return;
            }

            MessageBoxResult choice = MessageBox.Show(
                $"Remove {model.DisplayName} from this computer?\n\n" +
                "The desktop pet will keep working with built-in replies.",
                "Remove local AI model",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes) return;

            LocalAiStatusTxt.Text = "Removing model…";
            await LocalAiService.Instance.RemoveModelAsync(model.Id);
            RefreshLocalAiModelUi("Removed · you can download it again later.");
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes <= 0) return "unknown size";
            double gib = bytes / 1024d / 1024d / 1024d;
            return gib >= 1
                ? $"{gib:0.##} GB"
                : $"{bytes / 1024d / 1024d:0} MB";
        }

        private void OpenCloudAi_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://chat-agent.alirezalotfi.workers.dev/",
                UseShellExecute = true
            });
        }

        private void AddExclusion_Click(object sender, RoutedEventArgs e)
        {
            string app = AddExclusionTxt.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(app))
            {
                if (!app.EndsWith(".exe")) app += ".exe";
                if (!Exclusions.Contains(app))
                {
                    Exclusions.Add(app);
                    SaveSettings();
                }
                AddExclusionTxt.Clear();
            }
        }

        private void DeleteExclusion_Click(object sender, RoutedEventArgs e)
        {
            var btn = (Button)sender;
            var item = (string)btn.DataContext;
            Exclusions.Remove(item);
            SaveSettings();
        }

        private void ReplacementsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ReplacementsList.SelectedItem is ShortcutMapping selected)
            {
                var editWin = new EditMappingWindow(selected, false) { Owner = this };
                if (editWin.ShowDialog() == true)
                {
                    ReplacementsList.Items.Refresh();
                    SaveSettings();
                }
            }
        }

        private void ActionsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ActionsList.SelectedItem is ShortcutMapping selected)
            {
                var editWin = new EditMappingWindow(selected, true) { Owner = this };
                if (editWin.ShowDialog() == true)
                {
                    ActionsList.Items.Refresh();
                    SaveSettings();
                }
            }
        }

        private void SelectExclusionApps_Click(object sender, RoutedEventArgs e)
        {
            var picker = new ProcessPickerDialog { Owner = this };
            if (picker.ShowDialog() == true)
            {
                bool addedAny = false;
                foreach (var proc in picker.SelectedProcesses)
                {
                    string clean = proc.ToLower();
                    if (!Exclusions.Contains(clean))
                    {
                        Exclusions.Add(clean);
                        addedAny = true;
                    }
                }
                if (addedAny)
                {
                    SaveSettings();
                }
            }
        }

        private void Hook_OnPauseToggled(bool isPaused)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _trayManager.UpdateIconState(_hook.IsEnabled);
                UpdateHookStatusUI();

                if (isPaused)
                {
                    SoundManager.PlayCancel();
                    _trayManager.ShowNotification("KeyMapper Paused", "Keyboard interception has been paused.", System.Windows.Forms.ToolTipIcon.Info);
                }
                else
                {
                    SoundManager.PlaySuccess();
                    _trayManager.ShowNotification("KeyMapper Resumed", "Keyboard interception is active.", System.Windows.Forms.ToolTipIcon.Info);
                }
            }));
        }

        private void InsertRepMacro_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string macro)
            {
                int caretIndex = AddRepTargetTxt.CaretIndex;
                AddRepTargetTxt.Text = AddRepTargetTxt.Text.Insert(caretIndex, macro);
                AddRepTargetTxt.CaretIndex = caretIndex + macro.Length;
                AddRepTargetTxt.Focus();
            }
        }

        private void InsertActMacro_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string macro)
            {
                int caretIndex = AddActTargetTxt.CaretIndex;
                AddActTargetTxt.Text = AddActTargetTxt.Text.Insert(caretIndex, macro);
                AddActTargetTxt.CaretIndex = caretIndex + macro.Length;
                AddActTargetTxt.Focus();
            }
        }

        private void ExportMappings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                FileName = "keymapper_backup.json",
                Title = "Export Mappings Backup"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    var backup = new
                    {
                        Version = 2,
                        Replacements = Replacements.Select(x => new ShortcutConfig
                        {
                            Shortcut = x.Shortcut,
                            Target = x.Target,
                            IsAutoExpand = x.IsAutoExpand,
                            AllowedProcess = x.AllowedProcess,
                            ExcludedProcess = x.ExcludedProcess,
                            UsageCount = x.UsageCount
                        }).ToList(),
                        Actions = Actions.Select(x => new ShortcutConfig
                        {
                            Shortcut = x.Shortcut,
                            Target = x.Target,
                            AllowedProcess = x.AllowedProcess,
                            ExcludedProcess = x.ExcludedProcess,
                            UsageCount = x.UsageCount
                        }).ToList()
                    };

                    string json = System.Text.Json.JsonSerializer.Serialize(backup, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(dialog.FileName, json);
                    MessageBox.Show("Mappings exported successfully!", "Export Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to export backup: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ImportMappings_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "JSON Files (*.json)|*.json",
                Title = "Import Mappings Backup"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string json = File.ReadAllText(dialog.FileName);
                    
                    using (var doc = System.Text.Json.JsonDocument.Parse(json))
                    {
                        var root = doc.RootElement;
                        
                        List<ShortcutConfig> importedReps = new List<ShortcutConfig>();
                        List<ShortcutConfig> importedActs = new List<ShortcutConfig>();

                        if (root.TryGetProperty("Replacements", out var repsProp) && repsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in repsProp.EnumerateArray())
                            {
                                string shortcut = item.GetProperty("Shortcut").GetString() ?? "";
                                string target = item.GetProperty("Target").GetString() ?? "";
                                bool isAuto = item.TryGetProperty("IsAutoExpand", out var isAutoProp) && isAutoProp.GetBoolean();
                                string allowed = item.TryGetProperty("AllowedProcess", out var allowedProp) ? (allowedProp.GetString() ?? "") : "";
                                string excluded = item.TryGetProperty("ExcludedProcess", out var excludedProp) ? (excludedProp.GetString() ?? "") : "";
                                int usage = item.TryGetProperty("UsageCount", out var usageProp) ? usageProp.GetInt32() : 0;

                                importedReps.Add(new ShortcutConfig
                                {
                                    Shortcut = shortcut,
                                    Target = target,
                                    IsAutoExpand = isAuto,
                                    AllowedProcess = allowed,
                                    ExcludedProcess = excluded,
                                    UsageCount = usage
                                });
                            }
                        }

                        if (root.TryGetProperty("Actions", out var actsProp) && actsProp.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in actsProp.EnumerateArray())
                            {
                                string shortcut = item.GetProperty("Shortcut").GetString() ?? "";
                                string target = item.GetProperty("Target").GetString() ?? "";
                                string allowed = item.TryGetProperty("AllowedProcess", out var allowedProp) ? (allowedProp.GetString() ?? "") : "";
                                string excluded = item.TryGetProperty("ExcludedProcess", out var excludedProp) ? (excludedProp.GetString() ?? "") : "";
                                int usage = item.TryGetProperty("UsageCount", out var usageProp) ? usageProp.GetInt32() : 0;

                                importedActs.Add(new ShortcutConfig
                                {
                                    Shortcut = shortcut,
                                    Target = target,
                                    AllowedProcess = allowed,
                                    ExcludedProcess = excluded,
                                    UsageCount = usage
                                });
                            }
                        }

                        if (importedReps.Count == 0 && importedActs.Count == 0)
                        {
                            MessageBox.Show("No valid mappings found in backup file.", "Import Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        var result = MessageBox.Show($"Found {importedReps.Count} replacements and {importedActs.Count} actions. Do you want to merge them with your current settings?\n\n(Click 'Yes' to merge, 'No' to overwrite, 'Cancel' to abort)", "Import Options", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                        
                        if (result == MessageBoxResult.Cancel) return;

                        if (result == MessageBoxResult.No)
                        {
                            Replacements.Clear();
                            Actions.Clear();
                        }

                        foreach (var rep in importedReps)
                        {
                            var existing = Replacements.FirstOrDefault(x => x.Shortcut.Equals(rep.Shortcut, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                existing.Target = rep.Target;
                                existing.IsAutoExpand = rep.IsAutoExpand;
                                existing.AllowedProcess = rep.AllowedProcess;
                                existing.ExcludedProcess = rep.ExcludedProcess;
                            }
                            else
                            {
                                Replacements.Add(new ShortcutMapping
                                {
                                    Shortcut = rep.Shortcut,
                                    Target = rep.Target,
                                    IsAutoExpand = rep.IsAutoExpand,
                                    AllowedProcess = rep.AllowedProcess,
                                    ExcludedProcess = rep.ExcludedProcess,
                                    UsageCount = rep.UsageCount
                                });
                            }
                        }

                        foreach (var act in importedActs)
                        {
                            var existing = Actions.FirstOrDefault(x => x.Shortcut.Equals(act.Shortcut, StringComparison.OrdinalIgnoreCase));
                            if (existing != null)
                            {
                                existing.Target = act.Target;
                                existing.AllowedProcess = act.AllowedProcess;
                                existing.ExcludedProcess = act.ExcludedProcess;
                            }
                            else
                            {
                                Actions.Add(new ShortcutMapping
                                {
                                    Shortcut = act.Shortcut,
                                    Target = act.Target,
                                    AllowedProcess = act.AllowedProcess,
                                    ExcludedProcess = act.ExcludedProcess,
                                    UsageCount = act.UsageCount
                                });
                            }
                        }

                        ReplacementsList.Items.Refresh();
                        ActionsList.Items.Refresh();
                        SaveSettings();

                        MessageBox.Show("Mappings imported successfully!", "Import Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to import backup: {ex.Message}", "Import Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Window Lifetime & Minimize to Tray

        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_isShuttingDown)
            {
                e.Cancel = true;
                this.Hide(); // Hide window, keep running in background tray quietly
            }
            base.OnClosing(e);
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            _hook.Dispose();
            _mouseHook.Dispose();
            _overlayWindow.Close();
            _petOverlayWindow?.Close();
            _trayManager.Dispose();
            this.Close();
        }

        public void ShowStartupNotification()
        {
            _trayManager.ShowNotification("KeyMapper Active", "KeyMapper is running in your system tray. Double-click the icon to open settings.");
        }

        public void ShowPetOverlayWindow()
        {
            if (_petOverlayWindow != null)
            {
                _petOverlayWindow.Show();
                _petOverlayWindow.WindowState = WindowState.Normal;
                _petOverlayWindow.Topmost = true;
                _petOverlayWindow.Activate();
            }
        }

        public void HidePetOverlayWindow()
        {
            _petOverlayWindow?.Hide();
        }

        public void DisableKeyboardHookTemporarily()
        {
            _hook.IsEnabled = false;
        }

        public void EnableKeyboardHook()
        {
            _hook.IsEnabled = true;
            UpdateHookStatusUI();
        }

        public string ExecuteTextFunctionDirect(string fnName, string input)
        {
            return ExecuteTextFunction(fnName, input);
        }

        public async Task ProcessAndPerformReplacementDirect(ShortcutMapping mapping, string keyword, int backspaces)
        {
            await ProcessAndPerformReplacement(mapping, keyword, backspaces, null);
        }

        private async void Hook_OnShiftRightClick(int x, int y)
        {
            if (_isShuttingDown) return;

            string selectedText = "";
            try
            {
                selectedText = await GrabSelectionAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to grab selection for QuickAccess: {ex.Message}");
            }

            await Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Check if already open
                    foreach (Window window in Application.Current.Windows)
                    {
                        if (window is QuickAccessWindow)
                        {
                            window.Close();
                        }
                    }

                    var quickAccess = new QuickAccessWindow(this, selectedText, x, y);
                    quickAccess.Show();
                    quickAccess.Activate();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to open QuickAccess window: {ex.Message}");
                }
            }));
        }

        #endregion
    }

    public class ShortcutMapping
    {
        public string Shortcut { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public bool IsAutoExpand { get; set; } = false;
        public string AllowedProcess { get; set; } = string.Empty;
        public string ExcludedProcess { get; set; } = string.Empty;
        public int UsageCount { get; set; }

        public string AllowedProcessDisplay => string.IsNullOrEmpty(AllowedProcess) ? "Global" : AllowedProcess;
        public string ExcludedProcessDisplay => string.IsNullOrEmpty(ExcludedProcess) ? "None" : ExcludedProcess;
    }
}
