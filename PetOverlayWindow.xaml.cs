using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class PetOverlayWindow : Window
    {
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int WS_EX_LAYERED = 0x00080000;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hWnd);

        private readonly PetStateMachine _stateMachine;
        private readonly DispatcherTimer _speechBubbleTimer;
        private readonly DispatcherTimer _behaviorTimer;
        private readonly DispatcherTimer _musicTimer;
        private readonly Random _random = new Random();
        private IReadOnlyList<BitmapSource> _idleFrames = Array.Empty<BitmapSource>();
        private IReadOnlyList<BitmapSource> _walkFrames = Array.Empty<BitmapSource>();
        private Storyboard? _idleStoryboard;
        private Point? _wanderTarget;
        private DateTime _nextWanderAt = DateTime.MinValue;
        private DateTime _lastSpriteFrameAt = DateTime.MinValue;
        private IntPtr _lastExternalWindow;
        private int _animationFrame;
        private bool _isDragging;
        private bool _isContextMenuOpen;
        private bool _facingRight = true;
        private bool _walkingEnabled = true;
        private double _walkingSpeed = 92;
        private int _idleAnimationIntervalMs = 430;
        private bool _commentsEnabled = true;
        private bool _aiAmbientCommentsEnabled = true;
        private string _commentFrequency = "Normal";
        private bool _isClickThrough;
        private PetPersonalityProfile _personality = PetPersonalities.For("Pink Monster");
        private DateTime _nextContextPollAt = DateTime.MinValue;
        private DateTime _contextStartedAt = DateTime.Now;
        private DateTime _nextObservationAt = DateTime.Now.AddSeconds(12);
        private string _activeContextKey = string.Empty;
        private bool _breakReminderShown;
        private string _lastCommentedTrackKey = string.Empty;
        private DateTime _lastMusicCommentAt = DateTime.MinValue;
        private bool _ambientAiBusy;

        public PetStateMachine StateMachine => _stateMachine;

        public PetOverlayWindow()
        {
            InitializeComponent();

            _stateMachine = new PetStateMachine();
            _stateMachine.StateChanged += StateMachine_StateChanged;

            _speechBubbleTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(6)
            };
            _speechBubbleTimer.Tick += (s, e) => HideSpeechBubble();

            _behaviorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(60)
            };
            _behaviorTimer.Tick += BehaviorTimer_Tick;

            _musicTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(8)
            };
            _musicTimer.Tick += MusicTimer_Tick;

            Loaded += PetOverlayWindow_Loaded;
            Closed += (s, e) =>
            {
                _behaviorTimer.Stop();
                _musicTimer.Stop();
            };
            ContextMenuOpening += (s, e) => _isContextMenuOpen = true;
            ContextMenuClosing += (s, e) =>
            {
                _isContextMenuOpen = false;
                _nextWanderAt = DateTime.Now.AddSeconds(2);
            };
        }

        private void PetOverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position prominently near center-right of screen
            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Max(50, workArea.Right - Width - 200);
            Top = Math.Max(50, (workArea.Height - Height) / 2);
            Topmost = true;

            // Load saved character preference
            var settings = ConfigManager.Load();
            _walkingSpeed = Math.Clamp(settings.PetWalkingSpeed, 25, 260);
            _idleAnimationIntervalMs = Math.Clamp(settings.PetIdleAnimationIntervalMs, 180, 1000);
            _commentsEnabled = settings.PetCommentsEnabled;
            _aiAmbientCommentsEnabled = settings.AiAmbientCommentsEnabled;
            _commentFrequency = NormalizeCommentFrequency(settings.PetCommentFrequency);
            UpdateCommentMenuState();
            SetCharacter(settings.CurrentCharacter ?? "Pink Monster");

            // Start idle animation
            if (FindResource("IdleAnimation") is Storyboard idleStory)
            {
                _idleStoryboard = idleStory;
                _idleStoryboard.Begin(this, true);
                ApplyIdleAnimationSpeed();
            }

            _nextWanderAt = DateTime.Now.AddSeconds(2);
            _behaviorTimer.Start();
            _musicTimer.Start();
        }

        public void SetCharacter(string characterName)
        {
            try
            {
                _personality = PetPersonalities.For(characterName);
                string folderName = characterName switch
                {
                    "Owlet Monster" => "OwletMonster",
                    "Dude Monster" => "DudeMonster",
                    _ => "PinkMonster"
                };
                string spritePrefix = folderName switch
                {
                    "OwletMonster" => "Owlet_Monster",
                    "DudeMonster" => "Dude_Monster",
                    _ => "Pink_Monster"
                };

                _idleFrames = LoadFrames(folderName, $"{spritePrefix}_Idle_4.png", 4);
                _walkFrames = LoadFrames(folderName, $"{spritePrefix}_Walk_6.png", 6);
                _animationFrame = 0;
                PetSpriteImage.Source = _idleFrames.Count > 0 ? _idleFrames[0] : null;

                _activeContextKey = string.Empty;
                _nextObservationAt =
                    DateTime.Now.AddSeconds(ScaleCommentDelay(12));
                ShowSpeechBubble(_personality.SpeakerName, _personality.Introduction(_random), 8);

                // Save setting
                var settings = ConfigManager.Load();
                settings.CurrentCharacter = characterName;
                ConfigManager.Save(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set character: {ex.Message}");
            }
        }

        public void SetClickThrough(bool enable)
        {
            _isClickThrough = enable;
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            if (enable)
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            }
            else
            {
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle & ~WS_EX_TRANSPARENT);
            }
        }

        private void StateMachine_StateChanged(object? sender, PetStateChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (e.StatusMessage != null)
                {
                    ShowSpeechBubble(_personality.SpeakerName, e.StatusMessage);
                }

                switch (e.NewState)
                {
                    case PetState.Listening:
                        StatusBadge.Visibility = Visibility.Visible;
                        StatusBadgeText.Text = "LISTEN";
                        break;
                    case PetState.Working:
                        StatusBadge.Visibility = Visibility.Visible;
                        StatusBadgeText.Text = "WORK";
                        break;
                    case PetState.Talking:
                        StatusBadge.Visibility = Visibility.Visible;
                        StatusBadgeText.Text = "AI";
                        break;
                    default:
                        StatusBadge.Visibility = Visibility.Collapsed;
                        break;
                }
            });
        }

        public void ShowSpeechBubble(string title, string message, int autoHideSeconds = 6)
        {
            Dispatcher.Invoke(() =>
            {
                SpeechBubbleTitle.Text = title;
                SpeechBubbleText.Text = message;
                SpeechBubble.Visibility = Visibility.Visible;

                _speechBubbleTimer.Stop();
                if (autoHideSeconds > 0)
                {
                    _speechBubbleTimer.Interval = TimeSpan.FromSeconds(autoHideSeconds);
                    _speechBubbleTimer.Start();
                }
            });
        }

        public void HideSpeechBubble()
        {
            Dispatcher.Invoke(() =>
            {
                _speechBubbleTimer.Stop();
                SpeechBubble.Visibility = Visibility.Collapsed;
            });
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                _isDragging = true;
                try
                {
                    DragMove();
                }
                finally
                {
                    _isDragging = false;
                    _wanderTarget = null;
                    _nextWanderAt = DateTime.Now.AddSeconds(3);
                }
            }
        }

        private IReadOnlyList<BitmapSource> LoadFrames(string folderName, string fileName, int frameCount)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Characters", folderName, fileName);
            if (!File.Exists(path)) return Array.Empty<BitmapSource>();

            var spriteSheet = new BitmapImage();
            spriteSheet.BeginInit();
            spriteSheet.UriSource = new Uri(path, UriKind.Absolute);
            spriteSheet.CacheOption = BitmapCacheOption.OnLoad;
            spriteSheet.EndInit();
            spriteSheet.Freeze();

            int frameWidth = spriteSheet.PixelWidth / frameCount;
            var frames = new List<BitmapSource>(frameCount);
            for (int i = 0; i < frameCount; i++)
            {
                var crop = new CroppedBitmap(
                    spriteSheet,
                    new Int32Rect(i * frameWidth, 0, frameWidth, spriteSheet.PixelHeight));
                crop.Freeze();

                // The source sheets place some feet on their final pixel row. Give
                // every character real transparent breathing room so no frame looks
                // clipped when WPF scales it.
                const int sidePadding = 3;
                const int topPadding = 2;
                const int bottomPadding = 3;
                var visual = new DrawingVisual();
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
                using (DrawingContext drawing = visual.RenderOpen())
                {
                    drawing.DrawImage(
                        crop,
                        new Rect(sidePadding, topPadding, frameWidth, spriteSheet.PixelHeight));
                }

                var paddedFrame = new RenderTargetBitmap(
                    frameWidth + (sidePadding * 2),
                    spriteSheet.PixelHeight + topPadding + bottomPadding,
                    96,
                    96,
                    PixelFormats.Pbgra32);
                paddedFrame.Render(visual);
                paddedFrame.Freeze();
                frames.Add(paddedFrame);
            }
            return frames;
        }

        private void BehaviorTimer_Tick(object? sender, EventArgs e)
        {
            RememberExternalForegroundWindow();

            if (!IsVisible || _isDragging || _isContextMenuOpen)
            {
                return;
            }

            if (_stateMachine.CurrentState != PetState.Idle)
            {
                // Work can pause wandering without freezing the character itself.
                // Keep the idle sprite cycling while OCR or another task runs.
                AdvanceSpriteFrame(false);
                return;
            }

            if (!_walkingEnabled)
            {
                _wanderTarget = null;
                AdvanceSpriteFrame(false);
                return;
            }

            bool isWalking = _wanderTarget.HasValue;
            if (!isWalking && DateTime.Now >= _nextWanderAt)
            {
                Rect workArea = SystemParameters.WorkArea;
                double maxLeft = Math.Max(workArea.Left + 8, workArea.Right - ActualWidth - 8);
                double maxTop = Math.Max(workArea.Top + 8, workArea.Bottom - ActualHeight - 8);
                _wanderTarget = new Point(
                    workArea.Left + 8 + _random.NextDouble() * (maxLeft - workArea.Left - 8),
                    workArea.Top + 8 + _random.NextDouble() * (maxTop - workArea.Top - 8));
                isWalking = true;
            }

            if (isWalking && _wanderTarget is Point target)
            {
                double step = _walkingSpeed * _personality.MovementMultiplier * _behaviorTimer.Interval.TotalSeconds;
                double dx = target.X - Left;
                double dy = target.Y - Top;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance <= step)
                {
                    Left = target.X;
                    Top = target.Y;
                    _wanderTarget = null;
                    _nextWanderAt = DateTime.Now.AddSeconds(
                        _personality.MinimumPauseSeconds +
                        _random.Next(Math.Max(
                            1,
                            _personality.MaximumPauseSeconds - _personality.MinimumPauseSeconds + 1)));
                    isWalking = false;
                }
                else
                {
                    _facingRight = dx >= 0;
                    PetSpriteImage.RenderTransform = new System.Windows.Media.ScaleTransform(_facingRight ? 1 : -1, 1, 60, 60);
                    Left += dx / distance * step;
                    Top += dy / distance * step;
                }
            }

            AdvanceSpriteFrame(isWalking);
        }

        private void AdvanceSpriteFrame(bool isWalking)
        {
            IReadOnlyList<BitmapSource> frames = isWalking ? _walkFrames : _idleFrames;
            if (frames.Count == 0) return;

            DateTime now = DateTime.UtcNow;
            TimeSpan frameInterval = isWalking
                ? TimeSpan.FromMilliseconds(115)
                : TimeSpan.FromMilliseconds(_idleAnimationIntervalMs);
            if (now - _lastSpriteFrameAt < frameInterval) return;

            _lastSpriteFrameAt = now;
            _animationFrame = (_animationFrame + 1) % frames.Count;
            PetSpriteImage.Source = frames[_animationFrame];
        }

        private void RememberExternalForegroundWindow()
        {
            DateTime now = DateTime.Now;
            if (now < _nextContextPollAt) return;
            _nextContextPollAt = now.AddSeconds(2);

            IntPtr foreground = GetForegroundWindow();
            if (foreground == IntPtr.Zero) return;

            GetWindowThreadProcessId(foreground, out uint processId);
            if (processId != (uint)Environment.ProcessId)
            {
                _lastExternalWindow = foreground;
                ObserveForegroundContext(foreground, processId, now);
            }
        }

        private void ObserveForegroundContext(IntPtr foreground, uint processId, DateTime now)
        {
            if (!_commentsEnabled) return;

            string windowTitle = ReadWindowTitle(foreground);
            if (string.IsNullOrWhiteSpace(windowTitle)) return;

            string processName;
            try
            {
                processName = Process.GetProcessById((int)processId).ProcessName;
            }
            catch
            {
                return;
            }

            string contextKey = $"{processName}|{windowTitle}";
            if (!string.Equals(contextKey, _activeContextKey, StringComparison.Ordinal))
            {
                _activeContextKey = contextKey;
                _contextStartedAt = now;
                _breakReminderShown = false;
                _nextObservationAt = now.AddSeconds(
                    ScaleCommentDelay(10 + _random.Next(8)));
                return;
            }

            if (SpeechBubble.Visibility == Visibility.Visible ||
                _isContextMenuOpen ||
                now < _nextObservationAt)
                return;

            if (!_breakReminderShown && now - _contextStartedAt >= TimeSpan.FromMinutes(25))
            {
                _breakReminderShown = true;
                ShowSpeechBubble(_personality.SpeakerName, _personality.BreakReminder(_random), 9);
            }
            else
            {
                var context = new ForegroundContext(processName, windowTitle);
                _ = ShowContextObservationAsync(context);
            }

            _nextObservationAt = now.AddSeconds(
                ScaleCommentDelay(
                    _personality.ObservationCooldownSeconds +
                    _random.Next(15, 31)));
        }

        private async void MusicTimer_Tick(object? sender, EventArgs e)
        {
            if (!_commentsEnabled ||
                !IsVisible ||
                _isDragging ||
                _isContextMenuOpen ||
                _stateMachine.CurrentState != PetState.Idle ||
                SpeechBubble.Visibility == Visibility.Visible)
            {
                return;
            }

            PlayingTrack? track =
                await MusicPresenceService.Instance.GetCurrentTrackAsync();
            if (track == null) return;

            DateTime now = DateTime.Now;
            bool sameTrack = string.Equals(
                track.Key,
                _lastCommentedTrackKey,
                StringComparison.Ordinal);
            double commentDelaySeconds = sameTrack ? 180 : 45;
            if (now - _lastMusicCommentAt <
                TimeSpan.FromSeconds(ScaleCommentDelay(commentDelaySeconds)))
            {
                return;
            }

            _lastCommentedTrackKey = track.Key;
            _lastMusicCommentAt = now;
            await ShowMusicObservationAsync(track);
        }

        private async Task ShowContextObservationAsync(ForegroundContext context)
        {
            string fallback = _personality.Observation(context, _random);
            string? generated = await TryCreateAmbientCommentAsync(
                $"{context.ProcessName} · {context.WindowTitle}",
                null,
                null);
            if (SpeechBubble.Visibility != Visibility.Visible &&
                !_isContextMenuOpen)
            {
                ShowSpeechBubble(
                    _personality.SpeakerName,
                    generated ?? fallback,
                    9);
            }
        }

        private async Task ShowMusicObservationAsync(PlayingTrack track)
        {
            string fallback = _personality.MusicObservation(
                track.Title,
                track.Artist,
                _random);
            string? generated = await TryCreateAmbientCommentAsync(
                _activeContextKey,
                track.Title,
                track.Artist);
            if (SpeechBubble.Visibility != Visibility.Visible &&
                !_isContextMenuOpen)
            {
                ShowSpeechBubble(
                    _personality.SpeakerName,
                    generated ?? fallback,
                    9);
            }
        }

        private async Task<string?> TryCreateAmbientCommentAsync(
            string visibleContext,
            string? musicTitle,
            string? musicArtist)
        {
            if (_ambientAiBusy) return null;

            AppSettings settings = ConfigManager.Load();
            if (!_aiAmbientCommentsEnabled ||
                !settings.AiAmbientCommentsEnabled ||
                !settings.LocalAiEnabled ||
                !LocalAiService.Instance.IsInstalled(settings.LocalAiModelId))
            {
                return null;
            }

            _ambientAiBusy = true;
            try
            {
                return await AiAssistantService.Instance.CreateAmbientCommentAsync(
                    _personality.CharacterName,
                    visibleContext,
                    musicTitle,
                    musicArtist,
                    settings);
            }
            finally
            {
                _ambientAiBusy = false;
            }
        }

        private static string ReadWindowTitle(IntPtr window)
        {
            int length = GetWindowTextLength(window);
            if (length <= 0) return string.Empty;
            var text = new StringBuilder(length + 1);
            GetWindowText(window, text, text.Capacity);
            return text.ToString();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isClickThrough)
            {
                // Subtle scale up on hover
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
        }

        private void MenuAsk_Click(object sender, RoutedEventArgs e)
        {
            ShowSpeechBubble(
                _personality.SpeakerName,
                _personality.ActionLine(PetAction.Command, _random));

            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is AssistantWindow existingAssistant)
                {
                    existingAssistant.Activate();
                    return;
                }
            }

            var assistant = new AssistantWindow(
                _personality.CharacterName,
                _activeContextKey)
            {
                Owner = this
            };
            assistant.Show();
        }

        private void MenuSetWalkingSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem item || !double.TryParse(item.Tag?.ToString(), out double speed))
            {
                return;
            }

            _walkingSpeed = speed;
            var settings = ConfigManager.Load();
            settings.PetWalkingSpeed = speed;
            ConfigManager.Save(settings);
            ShowSpeechBubble(
                _personality.SpeakerName,
                $"{item.Header} pace. {_personality.ActionLine(PetAction.WalkingOn, _random)}");
        }

        private void MenuSetIdleSpeed_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem item ||
                !int.TryParse(item.Tag?.ToString(), out int interval))
                return;

            _idleAnimationIntervalMs = Math.Clamp(interval, 180, 1000);
            AppSettings settings = ConfigManager.Load();
            settings.PetIdleAnimationIntervalMs = _idleAnimationIntervalMs;
            ConfigManager.Save(settings);
            ApplyIdleAnimationSpeed();
            ShowSpeechBubble(
                _personality.SpeakerName,
                $"{item.Header} idle rhythm selected.");
        }

        private void MenuToggleComments_Click(object sender, RoutedEventArgs e)
        {
            _commentsEnabled = !_commentsEnabled;
            AppSettings settings = ConfigManager.Load();
            settings.PetCommentsEnabled = _commentsEnabled;
            ConfigManager.Save(settings);
            UpdateCommentMenuState();

            ShowSpeechBubble(
                _personality.SpeakerName,
                _commentsEnabled
                    ? "Comments are back on. I’ll speak only at the pace you selected."
                    : "Comments are off. I’ll stay quiet unless you ask me something.",
                6);
        }

        private void MenuSetCommentFrequency_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.MenuItem item ||
                item.Tag is not string frequency)
            {
                return;
            }

            _commentFrequency = NormalizeCommentFrequency(frequency);
            AppSettings settings = ConfigManager.Load();
            settings.PetCommentFrequency = _commentFrequency;
            ConfigManager.Save(settings);
            UpdateCommentMenuState();
            _nextObservationAt =
                DateTime.Now.AddSeconds(ScaleCommentDelay(18));

            ShowSpeechBubble(
                _personality.SpeakerName,
                $"{_commentFrequency} comment pace selected.",
                5);
        }

        private void MenuToggleAiComments_Click(object sender, RoutedEventArgs e)
        {
            _aiAmbientCommentsEnabled = !_aiAmbientCommentsEnabled;
            AppSettings settings = ConfigManager.Load();
            settings.AiAmbientCommentsEnabled = _aiAmbientCommentsEnabled;
            ConfigManager.Save(settings);
            UpdateCommentMenuState();

            string message = _aiAmbientCommentsEnabled
                ? LocalAiService.Instance.IsInstalled(settings.LocalAiModelId)
                    ? "Fresh local-AI comments are on. Nothing is sent to a hosted service."
                    : "Fresh AI comments are enabled. Download a local model in Settings to start them."
                : "Fresh AI comments are off. My built-in personality comments will still work.";
            ShowSpeechBubble(_personality.SpeakerName, message, 7);
        }

        private void UpdateCommentMenuState()
        {
            CommentsToggleMenuItem.Header =
                _commentsEnabled ? "Comments: On" : "Comments: Off";
            AiCommentsToggleMenuItem.Header =
                _aiAmbientCommentsEnabled
                    ? "Fresh AI Comments: On"
                    : "Fresh AI Comments: Off";
            CommentsQuietMenuItem.Header =
                _commentFrequency == "Quiet" ? "Quiet  ✓" : "Quiet";
            CommentsNormalMenuItem.Header =
                _commentFrequency == "Normal" ? "Normal  ✓" : "Normal";
            CommentsChattyMenuItem.Header =
                _commentFrequency == "Chatty" ? "Chatty  ✓" : "Chatty";
        }

        private double ScaleCommentDelay(double seconds) =>
            seconds * (_commentFrequency switch
            {
                "Quiet" => 2.2,
                "Chatty" => 0.48,
                _ => 1.0
            });

        private static string NormalizeCommentFrequency(string? frequency) =>
            frequency?.Trim() switch
            {
                "Quiet" => "Quiet",
                "Chatty" => "Chatty",
                _ => "Normal"
            };

        private void ApplyIdleAnimationSpeed()
        {
            if (_idleStoryboard == null) return;
            double speedRatio = 430.0 / _idleAnimationIntervalMs;
            _idleStoryboard.SetSpeedRatio(this, speedRatio);
        }

        private async void MenuDeGibberish_Click(object sender, RoutedEventArgs e)
        {
            await DeGibberishSelectedTextAsync(_lastExternalWindow);
        }

        public async Task DeGibberishSelectedTextAsync(IntPtr targetWindow)
        {
            _stateMachine.SetState(
                PetState.Working,
                _personality.ActionLine(PetAction.DeGibberish, _random));
            var result = await KeyboardLayoutConverter.Instance.ConvertSelectedTextLayoutAsync(targetWindow);
            _stateMachine.SetState(
                PetState.Idle,
                result.Success
                    ? $"De-gibberished into {result.Message.Replace("Converted to ", string.Empty)}:\n{result.Original}  →  {result.Corrected}"
                    : $"Select the gibberish text first, then choose De-gibberish.\n{result.Message}");
        }

        private async void MenuTranslate_Click(object sender, RoutedEventArgs e)
        {
            ShowSpeechBubble(
                _personality.SpeakerName,
                _personality.ActionLine(PetAction.Translate, _random));
            var selection = await KeyboardLayoutConverter.Instance.CaptureSelectedTextAsync(_lastExternalWindow);
            string initialText = selection.Success
                ? selection.Text
                : (Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty);

            var translator = new TranslatorWindow(initialText)
            {
                Owner = this
            };
            translator.Show();
        }

        private void MenuOcr_Click(object sender, RoutedEventArgs e)
        {
            ShowSpeechBubble(
                _personality.SpeakerName,
                _personality.ActionLine(PetAction.Ocr, _random));
            ScreenSnipper.StartSnipping(async (bitmap) =>
            {
                try
                {
                    _stateMachine.SetState(PetState.Working);
                    ShowSpeechBubble(
                        _personality.SpeakerName,
                        "Reading Persian, German, and English text...",
                        4);

                    OcrRecognitionResult result =
                        await OcrService.RecognizeDetailedAsync(bitmap);
                    if (!result.Success)
                    {
                        ShowSpeechBubble(
                            _personality.SpeakerName,
                            result.ErrorMessage,
                            8);
                        return;
                    }

                    var resultWindow = new OcrResultWindow(result)
                    {
                        Owner = this
                    };
                    resultWindow.Show();
                    ShowSpeechBubble(
                        _personality.SpeakerName,
                        $"I found {result.Text.Length} characters. Review, copy, or translate them in the result window.",
                        8);
                }
                catch (Exception ex)
                {
                    ShowSpeechBubble(
                        _personality.SpeakerName,
                        $"OCR stopped safely: {ex.Message}",
                        8);
                }
                finally
                {
                    bitmap.Dispose();
                    // Working/Talking states pause wandering. Always return to idle
                    // after OCR so animation, walking, and the status badge recover.
                    _stateMachine.SetState(PetState.Idle);
                }
            });
        }

        private void MenuToggleWalking_Click(object sender, RoutedEventArgs e)
        {
            _walkingEnabled = !_walkingEnabled;
            _wanderTarget = null;
            _nextWanderAt = DateTime.Now.AddSeconds(2);

            if (sender is System.Windows.Controls.MenuItem item)
            {
                item.Header = _walkingEnabled ? "Walking: On" : "Walking: Off";
            }

            ShowSpeechBubble(
                _personality.SpeakerName,
                _personality.ActionLine(
                    _walkingEnabled ? PetAction.WalkingOn : PetAction.WalkingOff,
                    _random));
        }

        private void MenuSettings_Click(object sender, RoutedEventArgs e)
        {
            ShowSpeechBubble(
                _personality.SpeakerName,
                _personality.ActionLine(PetAction.Settings, _random));
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.Show();
                mainWin.Activate();
            }
        }

        private void MenuCharPink_Click(object sender, RoutedEventArgs e)
        {
            SetCharacter("Pink Monster");
        }

        private void MenuCharOwlet_Click(object sender, RoutedEventArgs e)
        {
            SetCharacter("Owlet Monster");
        }

        private void MenuCharDude_Click(object sender, RoutedEventArgs e)
        {
            SetCharacter("Dude Monster");
        }

        private void MenuHide_Click(object sender, RoutedEventArgs e)
        {
            Hide();
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }
    }
}
