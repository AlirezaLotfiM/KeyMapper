using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        private readonly PetStateMachine _stateMachine;
        private readonly DispatcherTimer _speechBubbleTimer;
        private readonly DispatcherTimer _behaviorTimer;
        private readonly DispatcherTimer _musicTimer;
        private readonly DispatcherTimer _musicNoteTimer;
        private readonly DispatcherTimer _musicOverlayHideTimer;
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
        private bool _horizontalOnlyWalking;
        private double? _horizontalWalkTop;
        private double _walkingSpeed = 92;
        private int _idleAnimationIntervalMs = 430;
        private bool _commentsEnabled = true;
        private bool _musicNotesEnabled = true;
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
        private bool _isListeningAnimationActive;
        private MusicTrackAnalysis? _currentMusicAnalysis;
        private string _activeMusicAnalysisPath = string.Empty;
        private string _lastRecordedMusicPath = string.Empty;
        private DateTime _lastRecordedMusicAt = DateTime.MinValue;
        private DateTime _musicPausedAt = DateTime.MinValue;
        private DateTime _lastStrongMusicReactionAt = DateTime.MinValue;
        private int _nextMusicBeatIndex;
        private int _musicBeatSequence;
        private static readonly Brush FoodOutlineBrush =
            CreateFrozenPixelBrush(91, 43, 25);
        private static readonly Brush MeatBrush =
            CreateFrozenPixelBrush(226, 96, 36);
        private static readonly Brush FoodHighlightBrush =
            CreateFrozenPixelBrush(255, 176, 61);
        private static readonly Brush BoneBrush =
            CreateFrozenPixelBrush(255, 239, 191);
        private static readonly Brush TakoyakiBrush =
            CreateFrozenPixelBrush(238, 139, 42);
        private static readonly Brush TakoyakiSauceBrush =
            CreateFrozenPixelBrush(128, 48, 25);
        private static readonly Brush TakoyakiGarnishBrush =
            CreateFrozenPixelBrush(45, 132, 72);

        private static Brush CreateFrozenPixelBrush(
            byte red,
            byte green,
            byte blue)
        {
            var brush = new SolidColorBrush(
                Color.FromRgb(red, green, blue));
            brush.Freeze();
            return brush;
        }

        private enum PixelMusicGlyph
        {
            EighthNote,
            QuarterNote,
            SixteenthNote,
            BeamedPair,
            Chord,
            BeatSpark,
            Equalizer,
            Wave,
            Star,
            Bolt,
            Moon,
            Meat,
            Takoyaki
        }

        private enum ListeningAction
        {
            Nod,
            Bounce,
            Sway,
            HeadBang,
            Jump
        }

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

            _musicNoteTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(620)
            };
            _musicNoteTimer.Tick += MusicNoteTimer_Tick;

            _musicOverlayHideTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(1500)
            };
            _musicOverlayHideTimer.Tick += (s, e) =>
            {
                _musicOverlayHideTimer.Stop();
                PetMusicControlsOverlay.Visibility = Visibility.Collapsed;
            };

            Loaded += PetOverlayWindow_Loaded;

            LocalAudioPlayerService.Instance.OnTrackChanged += track => Dispatcher.Invoke(() =>
            {
                UpdatePetMusicControlsUI();
                _ = BeginMusicExperienceAsync(track, true);
            });
            LocalAudioPlayerService.Instance.OnPlaybackStateChanged += isPlaying => Dispatcher.Invoke(() =>
            {
                UpdatePetMusicControlsUI();
                HandleMusicPlaybackStateChanged(isPlaying);
            });
            LocalAudioPlayerService.Instance.OnVolumeChanged += volumePercent => Dispatcher.Invoke(() =>
            {
                if (PetVolumeSlider != null && Math.Abs(PetVolumeSlider.Value - volumePercent) > 0.5)
                {
                    PetVolumeSlider.Value = volumePercent;
                }
            });

            // Connect Smart Reminders and System Health Alerts
            ReminderService.Instance.OnReminderTriggered += item =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SoundManager.PlayAlarmSound();
                    ShowPersistentReminderBubble(item.Note);
                }));
            };

            SystemHealthService.Instance.OnHourlyChime += hour =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!_commentsEnabled ||
                        !IsVisible ||
                        _isContextMenuOpen ||
                        SpeechBubble.Visibility == Visibility.Visible ||
                        (_commentFrequency == "Quiet" && _random.Next(100) >= 45))
                    {
                        return;
                    }

                    string comment = _personality.HourlyComment(hour, _random);
                    if (string.IsNullOrWhiteSpace(comment))
                    {
                        return;
                    }

                    SoundManager.PlayHourlyChime();
                    ShowSpeechBubble(_personality.SpeakerName, comment, 8);
                }));
            };

            SystemHealthService.Instance.OnSystemWarning += warning =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    SoundManager.PlayAlarmSound();
                    ShowSpeechBubble(_personality.SpeakerName, warning, 10);
                }));
            };
            Closed += (s, e) =>
            {
                _behaviorTimer.Stop();
                _musicTimer.Stop();
                _musicNoteTimer.Stop();
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
            _walkingEnabled = settings.PetWalkingEnabled;
            _idleAnimationIntervalMs = Math.Clamp(settings.PetIdleAnimationIntervalMs, 180, 1000);
            _commentsEnabled = settings.PetCommentsEnabled;
            _musicNotesEnabled = settings.PetMusicNotesEnabled;
            _aiAmbientCommentsEnabled = settings.AiAmbientCommentsEnabled;
            _commentFrequency = NormalizeCommentFrequency(settings.PetCommentFrequency);
            _horizontalOnlyWalking = settings.PetHorizontalOnlyWalking;
            if (settings.PetPositionLeft is double savedLeft &&
                settings.PetPositionTop is double savedTop &&
                double.IsFinite(savedLeft) &&
                double.IsFinite(savedTop))
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                double maximumLeft = Math.Max(0, screenWidth - Width);
                double maximumTop = Math.Max(0, screenHeight - Height);
                Left = Math.Clamp(savedLeft, 0, maximumLeft);
                Top = Math.Clamp(savedTop, 0, maximumTop);
            }
            _horizontalWalkTop = Top;
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
            _musicNoteTimer.Start();
            UpdateMusicListeningState();
            if (LocalAudioPlayerService.Instance.CurrentTrack is AudioTrackItem track)
            {
                _ = BeginMusicExperienceAsync(track, false);
            }
        }

        private void MusicNoteTimer_Tick(object? sender, EventArgs e)
        {
            if (!_musicNotesEnabled ||
                !IsVisible ||
                !LocalAudioPlayerService.Instance.IsPlaying ||
                LocalAudioPlayerService.Instance.CurrentTrack == null ||
                SpeechBubble.Visibility == Visibility.Visible ||
                PetMusicControlsOverlay.Visibility == Visibility.Visible ||
                _isContextMenuOpen ||
                MusicNotesCanvas.Children.Count >= 7 ||
                _random.NextDouble() < 0.12)
            {
                return;
            }

            SpawnPixelMusicNote();
        }

        private void SpawnPixelMusicNote(
            PixelMusicGlyph? preferredGlyph = null)
        {
            int burstSize = preferredGlyph.HasValue
                ? 1
                : _random.NextDouble() < 0.24 ? 2 : 1;
            for (int burstIndex = 0;
                 burstIndex < burstSize &&
                 MusicNotesCanvas.Children.Count < 7;
                 burstIndex++)
            {
                PixelMusicGlyph glyph =
                    preferredGlyph ?? SelectMoodMusicGlyph();
                Canvas note = CreatePixelMusicGlyph(glyph);
                bool spawnOnRight = _random.NextDouble() > 0.5;
                double startLeft = spawnOnRight
                    ? _random.Next(98, 121)
                    : _random.Next(7, 33);
                double startTop = _random.Next(72, 108) + (burstIndex * 8);
                double direction = spawnOnRight ? 1 : -1;

                Canvas.SetLeft(note, startLeft);
                Canvas.SetTop(note, startTop);
                MusicNotesCanvas.Children.Add(note);

                int steps = _random.Next(7, 11);
                double durationSeconds = 2.35 + (_random.NextDouble() * 1.45);
                var leftAnimation = new DoubleAnimationUsingKeyFrames();
                var topAnimation = new DoubleAnimationUsingKeyFrames();

                for (int step = 0; step <= steps; step++)
                {
                    double progress = step / (double)steps;
                    TimeSpan time = TimeSpan.FromSeconds(
                        durationSeconds * progress);
                    double sway = ((step % 2 == 0) ? -1 : 1) *
                                  (2 + (progress * 3));
                    leftAnimation.KeyFrames.Add(
                        new DiscreteDoubleKeyFrame(
                            startLeft + (direction * progress * 11) + sway,
                            time));
                    topAnimation.KeyFrames.Add(
                        new DiscreteDoubleKeyFrame(
                            startTop - (progress * 76),
                            time));
                }

                var opacityAnimation = new DoubleAnimationUsingKeyFrames();
                opacityAnimation.KeyFrames.Add(
                    new LinearDoubleKeyFrame(0, TimeSpan.Zero));
                opacityAnimation.KeyFrames.Add(
                    new LinearDoubleKeyFrame(
                        0.94,
                        TimeSpan.FromSeconds(durationSeconds * 0.12)));
                opacityAnimation.KeyFrames.Add(
                    new LinearDoubleKeyFrame(
                        0.82,
                        TimeSpan.FromSeconds(durationSeconds * 0.68)));
                opacityAnimation.KeyFrames.Add(
                    new LinearDoubleKeyFrame(
                        0,
                        TimeSpan.FromSeconds(durationSeconds)));
                opacityAnimation.Completed += (_, _) =>
                    MusicNotesCanvas.Children.Remove(note);

                note.BeginAnimation(Canvas.LeftProperty, leftAnimation);
                note.BeginAnimation(Canvas.TopProperty, topAnimation);
                note.BeginAnimation(OpacityProperty, opacityAnimation);
            }
        }

        private Canvas CreatePixelMusicGlyph(PixelMusicGlyph glyph)
        {
            Brush noteBrush = (Brush)FindResource(
                _random.NextDouble() > 0.24
                    ? "AppAccentBrush"
                    : "AppTextBrush");
            var note = new Canvas
            {
                Width = 20,
                Height = 20,
                SnapsToDevicePixels = true
            };
            RenderOptions.SetEdgeMode(note, EdgeMode.Aliased);

            void AddPixel(
                double left,
                double top,
                double width,
                double height,
                Brush? fill = null)
            {
                var pixel = new System.Windows.Shapes.Rectangle
                {
                    Width = width,
                    Height = height,
                    Fill = fill ?? noteBrush,
                    SnapsToDevicePixels = true
                };
                RenderOptions.SetEdgeMode(pixel, EdgeMode.Aliased);
                Canvas.SetLeft(pixel, left);
                Canvas.SetTop(pixel, top);
                note.Children.Add(pixel);
            }

            switch (glyph)
            {
                case PixelMusicGlyph.EighthNote:
                    AddPixel(6, 2, 3, 12);
                    AddPixel(8, 2, 7, 3);
                    AddPixel(2, 12, 7, 5);
                    break;
                case PixelMusicGlyph.QuarterNote:
                    AddPixel(9, 2, 3, 12);
                    AddPixel(4, 12, 8, 6);
                    break;
                case PixelMusicGlyph.SixteenthNote:
                    AddPixel(6, 1, 3, 13);
                    AddPixel(8, 2, 7, 3);
                    AddPixel(8, 7, 6, 3);
                    AddPixel(1, 12, 8, 6);
                    break;
                case PixelMusicGlyph.BeamedPair:
                    AddPixel(3, 2, 13, 3);
                    AddPixel(3, 4, 3, 10);
                    AddPixel(13, 4, 3, 10);
                    AddPixel(0, 12, 7, 5);
                    AddPixel(10, 12, 7, 5);
                    break;
                case PixelMusicGlyph.Chord:
                    AddPixel(10, 2, 3, 13);
                    AddPixel(5, 8, 8, 5);
                    AddPixel(3, 13, 8, 5);
                    break;
                case PixelMusicGlyph.BeatSpark:
                    AddPixel(8, 1, 3, 6);
                    AddPixel(8, 13, 3, 6);
                    AddPixel(1, 8, 6, 3);
                    AddPixel(13, 8, 6, 3);
                    AddPixel(8, 8, 3, 3);
                    break;
                case PixelMusicGlyph.Equalizer:
                    AddPixel(2, 9, 3, 9);
                    AddPixel(7, 4, 3, 14);
                    AddPixel(12, 7, 3, 11);
                    AddPixel(17, 11, 3, 7);
                    break;
                case PixelMusicGlyph.Wave:
                    AddPixel(1, 9, 4, 3);
                    AddPixel(5, 6, 3, 3);
                    AddPixel(8, 3, 3, 3);
                    AddPixel(11, 6, 3, 3);
                    AddPixel(14, 9, 4, 3);
                    AddPixel(8, 12, 3, 5);
                    break;
                case PixelMusicGlyph.Star:
                    AddPixel(8, 1, 4, 6);
                    AddPixel(1, 8, 18, 4);
                    AddPixel(5, 5, 10, 10);
                    AddPixel(7, 14, 6, 5);
                    break;
                case PixelMusicGlyph.Bolt:
                    AddPixel(10, 1, 7, 4);
                    AddPixel(7, 5, 7, 5);
                    AddPixel(4, 10, 7, 4);
                    AddPixel(2, 14, 6, 4);
                    break;
                case PixelMusicGlyph.Moon:
                    AddPixel(6, 2, 8, 3);
                    AddPixel(3, 5, 7, 10);
                    AddPixel(6, 15, 8, 3);
                    AddPixel(10, 5, 5, 3);
                    AddPixel(10, 12, 5, 3);
                    break;
                case PixelMusicGlyph.Meat:
                {
                    AddPixel(6, 2, 9, 2, FoodOutlineBrush);
                    AddPixel(4, 4, 13, 3, FoodOutlineBrush);
                    AddPixel(3, 7, 14, 6, FoodOutlineBrush);
                    AddPixel(5, 13, 10, 3, FoodOutlineBrush);
                    AddPixel(5, 5, 10, 9, MeatBrush);
                    AddPixel(7, 4, 7, 3, MeatBrush);
                    AddPixel(5, 7, 4, 4, FoodHighlightBrush);
                    AddPixel(13, 13, 3, 3, FoodOutlineBrush);
                    AddPixel(15, 14, 3, 5, BoneBrush);
                    AddPixel(14, 17, 3, 3, BoneBrush);
                    AddPixel(17, 17, 3, 3, BoneBrush);
                    AddPixel(16, 16, 3, 3, FoodOutlineBrush);
                    break;
                }
                case PixelMusicGlyph.Takoyaki:
                {
                    AddPixel(5, 4, 10, 2, FoodOutlineBrush);
                    AddPixel(3, 6, 14, 9, FoodOutlineBrush);
                    AddPixel(5, 15, 10, 3, FoodOutlineBrush);
                    AddPixel(5, 6, 10, 10, TakoyakiBrush);
                    AddPixel(3, 9, 2, 4, TakoyakiBrush);
                    AddPixel(15, 9, 2, 4, TakoyakiBrush);
                    AddPixel(6, 7, 8, 3, TakoyakiSauceBrush);
                    AddPixel(4, 10, 5, 3, TakoyakiSauceBrush);
                    AddPixel(11, 11, 5, 3, TakoyakiSauceBrush);
                    AddPixel(6, 6, 3, 3, FoodHighlightBrush);
                    AddPixel(7, 11, 2, 2, TakoyakiGarnishBrush);
                    AddPixel(12, 8, 2, 2, TakoyakiGarnishBrush);
                    AddPixel(13, 2, 2, 5, BoneBrush);
                    AddPixel(15, 0, 2, 4, BoneBrush);
                    break;
                }
            }

            return note;
        }

        private PixelMusicGlyph SelectMoodMusicGlyph()
        {
            if (_personality.CharacterName == "Monkey D. Luffy")
            {
                return _random.NextDouble() < 0.58
                    ? PixelMusicGlyph.Meat
                    : PixelMusicGlyph.Takoyaki;
            }

            string genre =
                LocalAudioPlayerService.Instance.CurrentTrack?.Genre
                    .ToLowerInvariant() ?? string.Empty;
            if (genre.Contains("electro") ||
                genre.Contains("edm") ||
                genre.Contains("dance"))
            {
                return _random.NextDouble() < 0.55
                    ? PixelMusicGlyph.Equalizer
                    : PixelMusicGlyph.Bolt;
            }
            if (genre.Contains("rock") || genre.Contains("metal"))
            {
                return _random.NextDouble() < 0.55
                    ? PixelMusicGlyph.Bolt
                    : PixelMusicGlyph.SixteenthNote;
            }
            if (genre.Contains("classical") ||
                genre.Contains("ambient") ||
                genre.Contains("instrumental"))
            {
                return _random.NextDouble() < 0.55
                    ? PixelMusicGlyph.Moon
                    : PixelMusicGlyph.Chord;
            }
            if (genre.Contains("pop") || genre.Contains("funk"))
            {
                return _random.NextDouble() < 0.55
                    ? PixelMusicGlyph.Star
                    : PixelMusicGlyph.BeamedPair;
            }

            PixelMusicGlyph[] choices =
                (_currentMusicAnalysis?.Mood ?? MusicMood.Focused) switch
                {
                    MusicMood.Peaceful =>
                        [PixelMusicGlyph.QuarterNote,
                         PixelMusicGlyph.Moon,
                         PixelMusicGlyph.BeatSpark],
                    MusicMood.Melancholic =>
                        [PixelMusicGlyph.EighthNote,
                         PixelMusicGlyph.Wave,
                         PixelMusicGlyph.Moon],
                    MusicMood.Cheerful =>
                        [PixelMusicGlyph.BeamedPair,
                         PixelMusicGlyph.Chord,
                         PixelMusicGlyph.Star],
                    MusicMood.Dramatic =>
                        [PixelMusicGlyph.Chord,
                         PixelMusicGlyph.Star,
                         PixelMusicGlyph.Wave],
                    MusicMood.Intense =>
                        [PixelMusicGlyph.SixteenthNote,
                         PixelMusicGlyph.Equalizer,
                         PixelMusicGlyph.Bolt],
                    _ =>
                        [PixelMusicGlyph.EighthNote,
                         PixelMusicGlyph.BeamedPair,
                         PixelMusicGlyph.Equalizer,
                         PixelMusicGlyph.BeatSpark]
                };
            return choices[_random.Next(choices.Length)];
        }

        private void UpdateMusicListeningState()
        {
            bool shouldListen =
                _musicNotesEnabled &&
                LocalAudioPlayerService.Instance.IsPlaying &&
                LocalAudioPlayerService.Instance.CurrentTrack != null;
            _isListeningAnimationActive = shouldListen;
            if (!shouldListen)
            {
                MusicBounceTransform.BeginAnimation(
                    TranslateTransform.YProperty,
                    null);
                MusicSwayTransform.BeginAnimation(
                    RotateTransform.AngleProperty,
                    null);
                MusicGrooveScaleTransform.BeginAnimation(
                    ScaleTransform.ScaleXProperty,
                    null);
                MusicGrooveScaleTransform.BeginAnimation(
                    ScaleTransform.ScaleYProperty,
                    null);
                MusicBounceTransform.Y = 0;
                MusicSwayTransform.Angle = 0;
                MusicGrooveScaleTransform.ScaleX = 1;
                MusicGrooveScaleTransform.ScaleY = 1;
            }
        }

        private async Task BeginMusicExperienceAsync(
            AudioTrackItem? track,
            bool considerStartReaction)
        {
            if (track == null)
            {
                _currentMusicAnalysis = null;
                _activeMusicAnalysisPath = string.Empty;
                return;
            }

            string requestedPath = track.FilePath;
            bool isNewStart =
                considerStartReaction &&
                LocalAudioPlayerService.Instance.IsPlaying &&
                LocalAudioPlayerService.Instance.CurrentPosition <=
                    TimeSpan.FromSeconds(4) &&
                (!string.Equals(
                     requestedPath,
                     _lastRecordedMusicPath,
                     StringComparison.OrdinalIgnoreCase) ||
                 DateTime.Now - _lastRecordedMusicAt >
                    TimeSpan.FromSeconds(5));
            bool isRepeat = isNewStart &&
                            string.Equals(
                                requestedPath,
                                _lastRecordedMusicPath,
                                StringComparison.OrdinalIgnoreCase);
            PetTrackMemory? memory = null;
            if (isNewStart)
            {
                memory = MusicExperienceService.Instance.RecordTrackStart(
                    track,
                    _personality.CharacterName);
                _lastRecordedMusicPath = requestedPath;
                _lastRecordedMusicAt = DateTime.Now;
            }

            Task<MusicTrackAnalysis> analysisTask =
                MusicExperienceService.Instance.GetAnalysisAsync(track);
            MusicTrackAnalysis reactionAnalysis =
                MusicExperienceService.Instance.GetProvisionalAnalysis(track);
            _currentMusicAnalysis = reactionAnalysis;
            _activeMusicAnalysisPath = requestedPath;
            ResetMusicBeatCursor(
                LocalAudioPlayerService.Instance.CurrentPosition.TotalSeconds);
            UpdateMusicListeningState();

            if (isNewStart && _commentsEnabled)
            {
                await Task.Delay(900);
                if (SpeechBubble.Visibility != Visibility.Visible &&
                    !_isContextMenuOpen &&
                    LocalAudioPlayerService.Instance.IsPlaying &&
                    string.Equals(
                        LocalAudioPlayerService.Instance.CurrentTrack?.FilePath,
                        requestedPath,
                        StringComparison.OrdinalIgnoreCase))
                {
                    string reaction =
                        isRepeat
                            ? BuildRepeatReaction(track)
                            : BuildMusicStartReaction(
                                track,
                                reactionAnalysis,
                                memory!);
                    ShowSpeechBubble(
                        _personality.SpeakerName,
                        reaction,
                        8);
                    _lastCommentedTrackKey =
                        $"{track.DisplayTitle}-{track.DisplayArtist}";
                    _lastMusicCommentAt = DateTime.Now;
                }
            }

            MusicTrackAnalysis analysis = await analysisTask;
            if (LocalAudioPlayerService.Instance.CurrentTrack == null ||
                !string.Equals(
                    LocalAudioPlayerService.Instance.CurrentTrack.FilePath,
                    requestedPath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _currentMusicAnalysis = analysis;
            _activeMusicAnalysisPath = requestedPath;
            ResetMusicBeatCursor(
                LocalAudioPlayerService.Instance.CurrentPosition.TotalSeconds);
            UpdateMusicListeningState();
        }

        private void HandleMusicPlaybackStateChanged(bool isPlaying)
        {
            if (!isPlaying)
            {
                _musicPausedAt = DateTime.Now;
                UpdateMusicListeningState();
                return;
            }

            TimeSpan pauseDuration = _musicPausedAt == DateTime.MinValue
                ? TimeSpan.Zero
                : DateTime.Now - _musicPausedAt;
            _musicPausedAt = DateTime.MinValue;
            UpdateMusicListeningState();
            if (_currentMusicAnalysis == null &&
                LocalAudioPlayerService.Instance.CurrentTrack is
                    AudioTrackItem track)
            {
                _ = BeginMusicExperienceAsync(track, false);
            }

            if (pauseDuration >= TimeSpan.FromSeconds(30) &&
                _commentsEnabled &&
                SpeechBubble.Visibility != Visibility.Visible &&
                !_isContextMenuOpen)
            {
                ShowSpeechBubble(
                    _personality.SpeakerName,
                    BuildResumeReaction(),
                    7);
            }
        }

        private void UpdateMusicBeatReaction()
        {
            if (!_isListeningAnimationActive ||
                _currentMusicAnalysis == null ||
                _currentMusicAnalysis.Beats.Count == 0 ||
                !string.Equals(
                    _activeMusicAnalysisPath,
                    LocalAudioPlayerService.Instance.CurrentTrack?.FilePath,
                    StringComparison.OrdinalIgnoreCase) ||
                _isDragging ||
                _isContextMenuOpen ||
                _wanderTarget.HasValue)
            {
                return;
            }

            double position =
                LocalAudioPlayerService.Instance.CurrentPosition.TotalSeconds;
            IReadOnlyList<MusicBeat> beats = _currentMusicAnalysis.Beats;
            if (_nextMusicBeatIndex >= beats.Count ||
                (_nextMusicBeatIndex > 0 &&
                 beats[_nextMusicBeatIndex - 1].Seconds > position + 0.4))
            {
                ResetMusicBeatCursor(position);
            }

            while (_nextMusicBeatIndex < beats.Count &&
                   beats[_nextMusicBeatIndex].Seconds < position - 0.18)
            {
                _nextMusicBeatIndex++;
            }
            if (_nextMusicBeatIndex >= beats.Count) return;

            MusicBeat beat = beats[_nextMusicBeatIndex];
            if (beat.Seconds > position + 0.09) return;

            _nextMusicBeatIndex++;
            _musicBeatSequence++;
            bool strongBeat =
                beat.Strength >= 0.72 ||
                _musicBeatSequence % 4 == 1;
            bool quietRest =
                !strongBeat &&
                (_currentMusicAnalysis.Mood is MusicMood.Peaceful
                    or MusicMood.Melancholic) &&
                _random.NextDouble() < 0.46;
            if (!quietRest)
            {
                TriggerListeningAction(beat.Strength, strongBeat);
            }

            if (_musicNotesEnabled &&
                SpeechBubble.Visibility != Visibility.Visible &&
                PetMusicControlsOverlay.Visibility != Visibility.Visible &&
                MusicNotesCanvas.Children.Count < 7 &&
                (strongBeat || _random.NextDouble() < 0.2))
            {
                SpawnPixelMusicNote(
                    strongBeat
                        ? SelectStrongBeatGlyph()
                        : SelectMoodMusicGlyph());
            }

            if (beat.Strength >= 0.86 &&
                position >= 20 &&
                DateTime.Now - _lastStrongMusicReactionAt >
                    TimeSpan.FromSeconds(75) &&
                _commentsEnabled &&
                SpeechBubble.Visibility != Visibility.Visible &&
                !_isContextMenuOpen)
            {
                _lastStrongMusicReactionAt = DateTime.Now;
                ShowSpeechBubble(
                    _personality.SpeakerName,
                    BuildStrongSectionReaction(),
                    7);
            }
        }

        private void ResetMusicBeatCursor(double positionSeconds)
        {
            IReadOnlyList<MusicBeat>? beats = _currentMusicAnalysis?.Beats;
            if (beats == null)
            {
                _nextMusicBeatIndex = 0;
                return;
            }

            int low = 0;
            int high = beats.Count;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (beats[middle].Seconds < positionSeconds - 0.12)
                    low = middle + 1;
                else
                    high = middle;
            }
            _nextMusicBeatIndex = low;
        }

        private void TriggerListeningAction(
            double beatStrength,
            bool strongBeat)
        {
            ListeningAction action = SelectListeningAction(strongBeat);
            double intensity = 0.65 + (beatStrength * 0.55);
            double beatDuration = Math.Clamp(
                60 / (_currentMusicAnalysis?.BeatsPerMinute ?? 104) * 0.34,
                0.11,
                0.28);
            double bounce = 0;
            double sway = 0;
            double scale = 0;

            switch (action)
            {
                case ListeningAction.Nod:
                    bounce = 1.2 * intensity;
                    sway = 0.8 * intensity;
                    break;
                case ListeningAction.Bounce:
                    bounce = 2.7 * intensity;
                    scale = 0.012 * intensity;
                    break;
                case ListeningAction.Sway:
                    bounce = 1.1 * intensity;
                    sway = 2.5 * intensity;
                    break;
                case ListeningAction.HeadBang:
                    bounce = 2.1 * intensity;
                    sway = 3.1 * intensity;
                    scale = 0.01 * intensity;
                    break;
                case ListeningAction.Jump:
                    bounce = 4.2 * intensity;
                    sway = 1.6 * intensity;
                    scale = 0.025 * intensity;
                    break;
            }

            double direction = _musicBeatSequence % 2 == 0 ? 1 : -1;
            BeginBeatAnimation(
                MusicBounceTransform,
                TranslateTransform.YProperty,
                0,
                -bounce,
                beatDuration);
            BeginBeatAnimation(
                MusicSwayTransform,
                RotateTransform.AngleProperty,
                0,
                sway * direction,
                beatDuration);
            if (scale > 0)
            {
                BeginBeatAnimation(
                    MusicGrooveScaleTransform,
                    ScaleTransform.ScaleXProperty,
                    1,
                    1 + scale,
                    beatDuration);
                BeginBeatAnimation(
                    MusicGrooveScaleTransform,
                    ScaleTransform.ScaleYProperty,
                    1,
                    1 - (scale * 0.45),
                    beatDuration);
            }
        }

        private static void BeginBeatAnimation(
            Animatable target,
            DependencyProperty property,
            double from,
            double to,
            double durationSeconds)
        {
            var animation = new DoubleAnimation
            {
                From = from,
                To = to,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                AutoReverse = true,
                FillBehavior = FillBehavior.Stop,
                EasingFunction = new QuadraticEase
                {
                    EasingMode = EasingMode.EaseOut
                }
            };
            target.BeginAnimation(
                property,
                animation,
                HandoffBehavior.SnapshotAndReplace);
        }

        private ListeningAction SelectListeningAction(bool strongBeat)
        {
            ListeningAction[] choices = _personality.CharacterName switch
            {
                "Pink Monster" => strongBeat
                    ? [ListeningAction.Jump, ListeningAction.Bounce]
                    : [ListeningAction.Bounce,
                       ListeningAction.Sway,
                       ListeningAction.Nod],
                "Owlet Monster" => strongBeat
                    ? [ListeningAction.Bounce, ListeningAction.Nod]
                    : [ListeningAction.Nod,
                       ListeningAction.Nod,
                       ListeningAction.Sway],
                "Dude Monster" => strongBeat
                    ? [ListeningAction.HeadBang, ListeningAction.Sway]
                    : [ListeningAction.Sway,
                       ListeningAction.HeadBang,
                       ListeningAction.Nod],
                "Frieren" => strongBeat
                    ? [ListeningAction.Sway, ListeningAction.Nod]
                    : [ListeningAction.Nod,
                       ListeningAction.Nod,
                       ListeningAction.Sway],
                "Yuji Itadori" => strongBeat
                    ? [ListeningAction.Jump, ListeningAction.HeadBang]
                    : [ListeningAction.Bounce,
                       ListeningAction.HeadBang,
                       ListeningAction.Sway],
                "Monkey D. Luffy" => strongBeat
                    ? [ListeningAction.Jump,
                       ListeningAction.Jump,
                       ListeningAction.Sway]
                    : [ListeningAction.Bounce,
                       ListeningAction.Sway,
                       ListeningAction.Jump],
                _ => [ListeningAction.Nod, ListeningAction.Bounce]
            };
            return choices[_random.Next(choices.Length)];
        }

        private PixelMusicGlyph SelectStrongBeatGlyph()
        {
            if (_personality.CharacterName == "Monkey D. Luffy")
            {
                return _musicBeatSequence % 3 == 0
                    ? PixelMusicGlyph.Takoyaki
                    : PixelMusicGlyph.Meat;
            }

            return (_currentMusicAnalysis?.Mood ?? MusicMood.Focused) switch
            {
                MusicMood.Peaceful => PixelMusicGlyph.Moon,
                MusicMood.Melancholic => PixelMusicGlyph.Wave,
                MusicMood.Cheerful => PixelMusicGlyph.Star,
                MusicMood.Dramatic => PixelMusicGlyph.Chord,
                MusicMood.Intense => PixelMusicGlyph.Bolt,
                _ => PixelMusicGlyph.Equalizer
            };
        }

        private string BuildMusicStartReaction(
            AudioTrackItem track,
            MusicTrackAnalysis analysis,
            PetTrackMemory memory)
        {
            int listens = memory.CharacterListens.TryGetValue(
                _personality.CharacterName,
                out int rememberedListens)
                ? rememberedListens
                : 1;
            bool remembered = listens > 1;
            int affinity = MusicExperienceService.Instance
                .GetCharacterAffinity(
                    _personality.CharacterName,
                    track.Genre,
                    analysis.Mood);
            string mood = analysis.Mood.ToString().ToLowerInvariant();

            if (track.IsFavorite)
            {
                return _personality.CharacterName switch
                {
                    "Pink Monster" =>
                        $"A favorite is back! “{track.DisplayTitle}” already has my feet bouncing.",
                    "Owlet Monster" =>
                        $"A marked favorite. “{track.DisplayTitle}” deserves attentive listening.",
                    "Dude Monster" =>
                        $"Favorite track. Good call. “{track.DisplayTitle}” stays.",
                    "Frieren" =>
                        $"You kept this melody close. I understand why “{track.DisplayTitle}” returned.",
                    "Yuji Itadori" =>
                        $"Your favorite is on! “{track.DisplayTitle}” gets full energy!",
                    "Monkey D. Luffy" =>
                        $"A favorite song! Turn up “{track.DisplayTitle}” and start the feast!",
                    _ => $"A favorite returns: “{track.DisplayTitle}”."
                };
            }

            if (remembered)
            {
                return _personality.CharacterName switch
                {
                    "Pink Monster" =>
                        $"I remember “{track.DisplayTitle}”! It still paints the desktop {mood}.",
                    "Owlet Monster" =>
                        $"We have heard “{track.DisplayTitle}” before. Its {mood} shape is familiar now.",
                    "Dude Monster" =>
                        $"“{track.DisplayTitle}” again. Still works. No objections.",
                    "Frieren" =>
                        $"This melody has returned. “{track.DisplayTitle}” feels more familiar each time.",
                    "Yuji Itadori" =>
                        $"I know this one! “{track.DisplayTitle}” still hits with the same energy!",
                    "Monkey D. Luffy" =>
                        $"This song came back! “{track.DisplayTitle}” is part of the crew now!",
                    _ =>
                        $"I remember “{track.DisplayTitle}”. It is good to hear it again."
                };
            }

            if (affinity > 0)
            {
                return _personality.CharacterName switch
                {
                    "Pink Monster" =>
                        $"New song! “{track.DisplayTitle}” feels {mood}, and my feet voted yes.",
                    "Owlet Monster" =>
                        $"“{track.DisplayTitle}” suits me. The {mood} arrangement has room to breathe.",
                    "Dude Monster" =>
                        $"Now this is my kind of pulse. “{track.DisplayTitle}” can stay.",
                    "Frieren" =>
                        $"“{track.DisplayTitle}” has a {mood} atmosphere. I would listen a while.",
                    "Yuji Itadori" =>
                        $"Yes! “{track.DisplayTitle}” has exactly the energy I wanted!",
                    "Monkey D. Luffy" =>
                        $"This one sounds like an adventure! “{track.DisplayTitle}” is loud-crew approved!",
                    _ => $"“{track.DisplayTitle}” matches my mood."
                };
            }

            if (affinity < 0)
            {
                return _personality.CharacterName switch
                {
                    "Owlet Monster" =>
                        $"“{track.DisplayTitle}” is more forceful than my usual choice. I shall study the rhythm.",
                    "Frieren" =>
                        $"This is not my usual kind of music, but unfamiliar songs can still reveal something.",
                    "Dude Monster" =>
                        $"Very calm. Not my first pick, but I can work with it.",
                    _ =>
                        $"“{track.DisplayTitle}” is outside my usual taste. Let us see where it goes."
                };
            }

            return _personality.CharacterName switch
            {
                "Pink Monster" =>
                    $"First listen! “{track.DisplayTitle}” feels {mood}. I am investigating with my feet.",
                "Owlet Monster" =>
                    $"A new piece, “{track.DisplayTitle}”. I am listening for its structure.",
                "Dude Monster" =>
                    $"New track: “{track.DisplayTitle}”. Let us see if it earns the repeat.",
                "Frieren" =>
                    $"I have not heard “{track.DisplayTitle}” before. New melodies are small journeys.",
                "Yuji Itadori" =>
                    $"New track! “{track.DisplayTitle}” is getting a fair, full-energy listen!",
                "Monkey D. Luffy" =>
                    $"A brand-new song! “{track.DisplayTitle}” might be our next adventure anthem!",
                _ => $"Now listening to “{track.DisplayTitle}” by {track.DisplayArtist}."
            };
        }

        private string BuildRepeatReaction(AudioTrackItem track) =>
            _personality.CharacterName switch
            {
                "Pink Monster" =>
                    $"Again! “{track.DisplayTitle}” found the replay button and jumped on it.",
                "Owlet Monster" =>
                    $"Repeating “{track.DisplayTitle}”. A second pass often reveals quieter details.",
                "Dude Monster" =>
                    $"Immediate repeat. “{track.DisplayTitle}” clearly did its job.",
                "Frieren" =>
                    $"The melody begins again. Some moments deserve more time than one passage allows.",
                "Yuji Itadori" =>
                    $"Run it back! “{track.DisplayTitle}” gets another round!",
                "Monkey D. Luffy" =>
                    $"Again! A good song should circle the whole ship twice!",
                _ => $"Playing “{track.DisplayTitle}” again."
            };

        private string BuildResumeReaction() =>
            _personality.CharacterName switch
            {
                "Pink Monster" =>
                    "Music is back! I kept the rhythm somewhere safe.",
                "Owlet Monster" =>
                    "Playback resumed. The pause made the returning details clearer.",
                "Dude Monster" =>
                    "Music back on. Good. The room was getting too quiet.",
                "Frieren" =>
                    "The melody continues after its rest. It did not lose its place.",
                "Yuji Itadori" =>
                    "We are back! The song still has plenty of energy!",
                "Monkey D. Luffy" =>
                    "The music is back! Continue the party!",
                _ => "Music resumed."
            };

        private string BuildStrongSectionReaction() =>
            _personality.CharacterName switch
            {
                "Pink Monster" =>
                    "Oh! This part just grew bigger. My tiny dance has become official.",
                "Owlet Monster" =>
                    "The arrangement just opened up. That stronger layer was worth waiting for.",
                "Dude Monster" =>
                    "There it is. That section has some weight.",
                "Frieren" =>
                    "The music has reached a stronger passage. It feels like a spell completing.",
                "Yuji Itadori" =>
                    "This part hits hard! Now we are moving!",
                "Monkey D. Luffy" =>
                    "Here comes the big part! Everybody on deck!",
                _ => "This section just became more intense."
            };

        public void SetCharacter(string characterName)
        {
            try
            {
                _personality = PetPersonalities.For(characterName);
                string folderName = characterName switch
                {
                    "Owlet Monster" => "OwletMonster",
                    "Dude Monster" => "DudeMonster",
                    "Frieren" => "Frieren",
                    "Yuji Itadori" => "Yuji",
                    "Monkey D. Luffy" => "Luffy",
                    _ => "PinkMonster"
                };
                string spritePrefix = folderName switch
                {
                    "OwletMonster" => "Owlet_Monster",
                    "DudeMonster" => "Dude_Monster",
                    "Frieren" => "Frieren",
                    "Yuji" => "Yuji",
                    "Luffy" => "Luffy",
                    _ => "Pink_Monster"
                };

                _idleFrames = LoadFrames(
                    folderName,
                    $"{spritePrefix}_Idle_4.png",
                    4);
                _walkFrames = LoadFrames(
                    folderName,
                    $"{spritePrefix}_Walk_6.png",
                    6);
                _animationFrame = 0;
                PetSpriteImage.Source = _idleFrames.Count > 0 ? _idleFrames[0] : null;
                _isListeningAnimationActive = false;
                UpdateMusicListeningState();

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

        private void SetAutoFlowDirection(string text)
        {
            bool hasPersian = System.Text.RegularExpressions.Regex.IsMatch(text ?? "", @"[\u0600-\u06FF]");
            FlowDirection dir = hasPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            SpeechBubbleText.FlowDirection = dir;
            SpeechBubbleTitle.FlowDirection = dir;
        }

        public void ShowSpeechBubble(string title, string message, int autoHideSeconds = 6)
        {
            Dispatcher.Invoke(() =>
            {
                KeepPetSurfacesInsideWorkArea();
                SetAutoFlowDirection(message);
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
                    KeepPetSurfacesInsideWorkArea();
                    _horizontalWalkTop = Top;
                    _nextWanderAt = DateTime.Now.AddSeconds(3);
                    AppSettings settings = ConfigManager.Load();
                    settings.PetPositionLeft = Left;
                    settings.PetPositionTop = Top;
                    ConfigManager.Save(settings);
                }
            }
        }

        private IReadOnlyList<BitmapSource> LoadFrames(
            string folderName,
            string fileName,
            int frameCount)
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
                AdvanceSpriteFrame(false);
                return;
            }

            UpdateMusicBeatReaction();

            // 1. Mouse Curiosity: track mouse cursor direction when idle
            if (!_wanderTarget.HasValue && GetCursorPos(out POINT mousePos))
            {
                bool mouseOnRight = mousePos.X >= Left + (ActualWidth / 2);
                if (mouseOnRight != _facingRight)
                {
                    _facingRight = mouseOnRight;
                    PetSpriteImage.RenderTransform = new System.Windows.Media.ScaleTransform(_facingRight ? 1 : -1, 1, 60, 60);
                }
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
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;

                // Check if active foreground window is available to sit/walk on its top edge
                if (!_horizontalOnlyWalking &&
                    _lastExternalWindow != IntPtr.Zero &&
                    GetWindowRect(_lastExternalWindow, out RECT winRect))
                {
                    int winWidth = winRect.Right - winRect.Left;
                    int winHeight = winRect.Bottom - winRect.Top;
                    if (winWidth > 200 && winHeight > 150 && winRect.Top > 20 && winRect.Top < screenHeight - 100)
                    {
                        double targetX = winRect.Left + _random.Next(20, Math.Max(30, winWidth - 100));
                        double targetY = Math.Max(workArea.Top + 10, winRect.Top - ActualHeight + 6);
                        _wanderTarget = new Point(targetX, targetY);
                        isWalking = true;
                    }
                }

                if (!isWalking)
                {
                    double maxLeft = Math.Max(10, screenWidth - ActualWidth - 10);
                    // 35% chance to wander on the taskbar surface if horizontal walking is not locked
                    bool chooseTaskbar = !_horizontalOnlyWalking && _random.NextDouble() < 0.35;
                    double maxTop = chooseTaskbar
                        ? Math.Max(10, screenHeight - ActualHeight - 4)
                        : Math.Max(workArea.Top + 8, screenHeight - ActualHeight - 8);

                    double targetY = _horizontalOnlyWalking
                        ? (_horizontalWalkTop ?? Top)
                        : chooseTaskbar
                            ? Math.Max(workArea.Bottom, screenHeight - ActualHeight - 6)
                            : (workArea.Top + 8 + _random.NextDouble() * (maxTop - workArea.Top - 8));

                    _wanderTarget = new Point(
                        10 + _random.NextDouble() * (maxLeft - 10),
                        targetY);
                    isWalking = true;
                }
            }

            if (isWalking && _wanderTarget is Point target)
            {
                double screenWidth = SystemParameters.PrimaryScreenWidth;
                double screenHeight = SystemParameters.PrimaryScreenHeight;
                double clampedTargetX = Math.Clamp(target.X, 10, screenWidth - ActualWidth - 10);
                double clampedTargetY = _horizontalOnlyWalking
                    ? (_horizontalWalkTop ?? Top)
                    : Math.Clamp(target.Y, 10, screenHeight - ActualHeight - 4);

                double step = _walkingSpeed * _personality.MovementMultiplier * _behaviorTimer.Interval.TotalSeconds;
                double dx = clampedTargetX - Left;
                double dy = clampedTargetY - Top;
                double distance = Math.Sqrt(dx * dx + dy * dy);

                if (distance <= step)
                {
                    Left = clampedTargetX;
                    Top = clampedTargetY;
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
                    if (!_horizontalOnlyWalking)
                    {
                        Top += dy / distance * step;
                    }
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

            PlayingTrack? track = null;
            if (LocalAudioPlayerService.Instance.IsPlaying && LocalAudioPlayerService.Instance.CurrentTrack != null)
            {
                var cur = LocalAudioPlayerService.Instance.CurrentTrack;
                track = new PlayingTrack(cur.DisplayTitle, cur.DisplayArtist, $"{cur.DisplayTitle}-{cur.DisplayArtist}");
            }
            else
            {
                track = await MusicPresenceService.Instance.GetCurrentTrackAsync();
            }
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
            TrackDetails details = await MusicGenreService.Instance.FetchTrackDetailsAsync(track.Title, track.Artist);
            string genreLabel = string.IsNullOrWhiteSpace(details.Genre) ? "" : $" (Genre: {details.Genre})";
            AudioTrackItem? localTrack =
                LocalAudioPlayerService.Instance.CurrentTrack;
            string experienceContext = string.Empty;
            if (localTrack != null &&
                _currentMusicAnalysis != null)
            {
                int listens = MusicExperienceService.Instance
                    .GetCharacterListenCount(
                        localTrack,
                        _personality.CharacterName);
                experienceContext =
                    $"; detected mood: {_currentMusicAnalysis.Mood}; " +
                    $"energy: {_currentMusicAnalysis.Energy:0.00}; " +
                    $"familiar to this character: {listens > 1}. " +
                    "React naturally without quoting measurements";
            }

            string fallback = _personality.MusicObservation(
                track.Title,
                $"{track.Artist}{genreLabel}",
                _random);
            string? generated = await TryCreateAmbientCommentAsync(
                _activeContextKey,
                $"{track.Title}{genreLabel}{experienceContext}",
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
                _musicOverlayHideTimer.Stop();
                KeepPetSurfacesInsideWorkArea();
                UpdatePetMusicControlsUI();
                PetMusicControlsOverlay.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            _musicOverlayHideTimer.Stop();
            _musicOverlayHideTimer.Start();
        }

        private void PetMusicControlsOverlay_MouseEnter(object sender, MouseEventArgs e)
        {
            _musicOverlayHideTimer.Stop();
            KeepPetSurfacesInsideWorkArea();
            PetMusicControlsOverlay.Visibility = Visibility.Visible;
        }

        private void KeepPetSurfacesInsideWorkArea()
        {
            if (!IsLoaded || OverlayRoot == null ||
                PetCharacterContainer == null)
            {
                return;
            }

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double windowWidth = ActualWidth > 0 ? ActualWidth : Width;
            double windowHeight = ActualHeight > 0 ? ActualHeight : Height;

            Point petCenterInWindow = PetCharacterContainer.TranslatePoint(
                new Point(
                    PetCharacterContainer.ActualWidth / 2,
                    PetCharacterContainer.ActualHeight / 2),
                this);
            double petCenterOnScreen = Left + petCenterInWindow.X;

            double maximumLeft = Math.Max(0, screenWidth - windowWidth);
            double maximumTop = Math.Max(0, screenHeight - windowHeight);
            Left = Math.Clamp(Left, 0, maximumLeft);
            Top = Math.Clamp(Top, 0, maximumTop);

            double rootWidth = OverlayRoot.ActualWidth > 0
                ? OverlayRoot.ActualWidth
                : Math.Max(140, windowWidth - 20);
            double petWidth = PetCharacterContainer.ActualWidth > 0
                ? PetCharacterContainer.ActualWidth
                : 140;
            double desiredLeftInRoot =
                petCenterOnScreen - Left - OverlayRoot.Margin.Left -
                (petWidth / 2);
            double maximumPetLeft = Math.Max(0, rootWidth - petWidth);

            PetCharacterContainer.HorizontalAlignment =
                HorizontalAlignment.Left;
            PetCharacterContainer.Margin = new Thickness(
                Math.Clamp(desiredLeftInRoot, 0, maximumPetLeft),
                0,
                0,
                0);
        }

        private void PetMusicControlsOverlay_MouseLeave(object sender, MouseEventArgs e)
        {
            _musicOverlayHideTimer.Stop();
            _musicOverlayHideTimer.Start();
        }

        private void UpdatePetMusicControlsUI()
        {
            var track = LocalAudioPlayerService.Instance.CurrentTrack;
            if (track != null)
            {
                PetTrackTitleTxt.Text = track.DisplayTitle;
            }
            else
            {
                PetTrackTitleTxt.Text = "Music Player";
            }
            bool isPlaying = LocalAudioPlayerService.Instance.IsPlaying;
            PetPlayIcon.Visibility =
                isPlaying ? Visibility.Collapsed : Visibility.Visible;
            PetPauseGlyph.Visibility =
                isPlaying ? Visibility.Visible : Visibility.Collapsed;
            PetVolumeSlider.Value = LocalAudioPlayerService.Instance.CurrentVolume * 100.0;
        }

        private void PetPrevBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayPrevious();
        }

        private void PetPlayPauseBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.TogglePlayPause();
        }

        private void PetNextBtn_Click(object sender, RoutedEventArgs e)
        {
            LocalAudioPlayerService.Instance.PlayNext();
        }

        private void PetVolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LocalAudioPlayerService.Instance.SetVolume(e.NewValue);
        }

        private void MenuMusicPlayer_Click(object sender, RoutedEventArgs e)
        {
            foreach (Window window in System.Windows.Application.Current.Windows)
            {
                if (window is MusicPlayerWidgetWindow player)
                {
                    player.Show();
                    player.WindowState = WindowState.Normal;
                    player.Activate();
                    return;
                }
            }
            var win = new MusicPlayerWidgetWindow();
            win.Show();
        }

        private void MenuAsk_Click(object sender, RoutedEventArgs e)
        {
            OpenConversation();
        }

        public void OpenConversation(Window? owner = null)
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
                Owner = owner ?? this
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

        private void MenuToggleMusicNotes_Click(
            object sender,
            RoutedEventArgs e)
        {
            _musicNotesEnabled = !_musicNotesEnabled;
            AppSettings settings = ConfigManager.Load();
            settings.PetMusicNotesEnabled = _musicNotesEnabled;
            ConfigManager.Save(settings);
            if (!_musicNotesEnabled)
            {
                MusicNotesCanvas.Children.Clear();
            }
            UpdateMusicListeningState();
            UpdateCommentMenuState();

            ShowSpeechBubble(
                _personality.SpeakerName,
                _musicNotesEnabled
                    ? "Music ambience is on. I’ll move with the rhythm and let it decorate the air."
                    : "Music ambience is off. I’ll listen without the visual groove.",
                5);
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
            MusicNotesToggleMenuItem.Header =
                _musicNotesEnabled
                    ? "Music Ambience: On"
                    : "Music Ambience: Off";
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
            if (HorizontalWalkingToggleMenuItem != null)
            {
                HorizontalWalkingToggleMenuItem.Header =
                    _horizontalOnlyWalking ? "Horizontal Only: On" : "Horizontal Only: Off";
            }
            if (WalkingToggleMenuItem != null)
            {
                WalkingToggleMenuItem.Header =
                    _walkingEnabled ? "Walking: On" : "Walking: Off";
            }
        }

        private void MenuToggleHorizontalWalking_Click(object sender, RoutedEventArgs e)
        {
            _horizontalOnlyWalking = !_horizontalOnlyWalking;
            _wanderTarget = null;
            _horizontalWalkTop = Top;
            _nextWanderAt = DateTime.Now.AddSeconds(1);
            var settings = ConfigManager.Load();
            settings.PetHorizontalOnlyWalking = _horizontalOnlyWalking;
            ConfigManager.Save(settings);
            UpdateCommentMenuState();
            ShowSpeechBubble(
                _personality.SpeakerName,
                _horizontalOnlyWalking
                    ? "Horizontal-only walking enabled. I'll stay on this line!"
                    : "Free movement enabled. I can walk everywhere!",
                5);
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
            AppSettings settings = ConfigManager.Load();
            settings.PetWalkingEnabled = _walkingEnabled;
            ConfigManager.Save(settings);

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

        private void MenuCharFrieren_Click(object sender, RoutedEventArgs e) => SetCharacter("Frieren");
        private void MenuCharYuji_Click(object sender, RoutedEventArgs e) => SetCharacter("Yuji Itadori");
        private void MenuCharLuffy_Click(object sender, RoutedEventArgs e) => SetCharacter("Monkey D. Luffy");

        private void MenuHide_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.HidePetOverlayWindow();
            }
            else
            {
                AppSettings settings = ConfigManager.Load();
                settings.ShowPetOverlay = false;
                ConfigManager.Save(settings);
                Hide();
            }
        }

        private void PetMusicOverlay_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Mark handled BEFORE opening — prevents the event from bubbling to
                // PetOverlay_MouseDoubleClick (Window level), which would call
                // ShowMusicPlayerWidget a second time and immediately toggle it closed.
                e.Handled = true;

                if (Application.Current.MainWindow is MainWindow mainWin)
                {
                    mainWin.ShowMusicPlayerWidget();
                }
            }
        }

        private void PetOverlay_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.ShowMusicPlayerWidget();
            }
        }

        private void MenuNewStickyNote_Click(object sender, RoutedEventArgs e) => StickyNoteManager.Instance.CreateNewNote();
        private void MenuShowAllStickyNotes_Click(object sender, RoutedEventArgs e) => StickyNoteManager.Instance.ShowAllNotes();
        private void MenuHideAllStickyNotes_Click(object sender, RoutedEventArgs e)
        {
            foreach (var note in StickyNoteManager.Instance.Notes.ToList())
            {
                StickyNoteManager.Instance.HideNote(note.Id);
            }
        }

        private void MenuManageStickyNotes_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.Show();
                mainWin.Activate();
            }
        }

        private void MenuExpansions_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWin)
            {
                mainWin.Show();
                mainWin.Activate();
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private void MenuExitApp_Click(object sender, RoutedEventArgs e)
        {
            System.Windows.Application.Current.Shutdown();
        }

        private string _activeReminderNote = string.Empty;

        private void ShowPersistentReminderBubble(string note)
        {
            _activeReminderNote = note;
            KeepPetSurfacesInsideWorkArea();
            SetAutoFlowDirection(note);
            SpeechBubbleTitle.Text = "⏰ Reminder Alert";
            SpeechBubbleText.Text = note;
            ReminderButtonsPanel.Visibility = Visibility.Visible;
            SpeechBubble.Visibility = Visibility.Visible;
            _speechBubbleTimer.Stop(); // Do NOT auto-hide. Wait for user input!
        }

        private void ReminderDismissBtn_Click(object sender, RoutedEventArgs e)
        {
            SpeechBubble.Visibility = Visibility.Collapsed;
            ReminderButtonsPanel.Visibility = Visibility.Collapsed;
        }

        private void ReminderSnoozeBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(_activeReminderNote))
            {
                ReminderService.Instance.AddReminder(_activeReminderNote, TimeSpan.FromMinutes(5));
            }
            SpeechBubble.Visibility = Visibility.Collapsed;
            ReminderButtonsPanel.Visibility = Visibility.Collapsed;
        }
    }
}
