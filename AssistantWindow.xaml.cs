using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class AssistantWindow : Window
    {
        private readonly string _characterName;
        private readonly string _speakerName;
        private readonly string _visibleContext;
        private readonly ConversationPersona _persona;
        private readonly ImageSource _portraitSource;
        private readonly List<ConversationTurn> _history = new();
        private readonly DispatcherTimer _typingTimer;
        private int _typingFrame;
        private bool _thinkingInPersian;
        private bool _clearMemoryArmed;
        private DateTime _clearMemoryArmedUntil;
        private bool _isBusy;

        public AssistantWindow(string characterName, string visibleContext = "")
        {
            InitializeComponent();

            _characterName = characterName;
            _speakerName = PetPersonalities.For(characterName).SpeakerName;
            _visibleContext = visibleContext.Trim();
            _persona = ConversationPersona.For(characterName, _speakerName);
            _portraitSource = LoadPortrait(_persona.PortraitPath);

            WindowTitleText.Text = _speakerName;
            CharacterRoleText.Text = _persona.Role;
            CharacterTaglineText.Text = _persona.Tagline;
            CharacterPortrait.Source = _portraitSource;
            ContextText.Text = string.IsNullOrWhiteSpace(_visibleContext)
                ? "Screen awareness is quiet until you open the conversation from another app."
                : $"Nearby on your screen: {Shorten(_visibleContext, 92)}";

            _history.AddRange(ConversationMemoryStore.Load(characterName));
            UpdateMemoryStatus();
            RefreshQuickPrompts();

            _typingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(380)
            };
            _typingTimer.Tick += (sender, args) =>
            {
                _typingFrame = (_typingFrame + 1) % 4;
                TypingText.Text =
                    $"{_persona.ThinkingLine(_thinkingInPersian)}{new string('.', _typingFrame)}";
            };

            Loaded += (sender, args) =>
            {
                AddAssistantMessage(
                    _history.Count > 0
                        ? _persona.WelcomeBack
                        : _persona.Greeting,
                    []);
                PromptTextBox.Focus();
            };

            Closed += (sender, args) => _typingTimer.Stop();
        }

        private async void SendButton_Click(object sender, RoutedEventArgs e) =>
            await SendCurrentPromptAsync();

        private async void QuickPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string prompt) return;
            PromptTextBox.Text = prompt;
            PromptTextBox.CaretIndex = PromptTextBox.Text.Length;
            await SendCurrentPromptAsync();
        }

        private async Task SendCurrentPromptAsync()
        {
            if (_isBusy) return;
            string prompt = PromptTextBox.Text.Trim();
            if (prompt.Length == 0) return;

            ResetClearMemoryConfirmation();
            AddUserMessage(prompt);
            PromptTextBox.Clear();
            SetBusy(true, ContainsPersian(prompt));

            try
            {
                AssistantReply reply =
                    await ConversationalAssistantService.Instance.ProcessAsync(
                        prompt,
                        _characterName,
                        _visibleContext,
                        _history);

                AddAssistantMessage(reply.Message, reply.Actions);
                _history.Add(new ConversationTurn("user", prompt));
                _history.Add(new ConversationTurn("assistant", reply.Message));
                TrimHistory();
                ConversationMemoryStore.Save(_characterName, _history);
                UpdateMemoryStatus();
                RefreshQuickPrompts(prompt, reply.Message);
            }
            catch (Exception ex)
            {
                AddAssistantMessage(
                    _thinkingInPersian
                        ? $"اینجا با خیال راحت متوقف شدم: {ex.Message}"
                        : $"I stopped safely: {ex.Message}",
                    []);
            }
            finally
            {
                SetBusy(false);
                PromptTextBox.Focus();
            }
        }

        private void AddUserMessage(string message)
        {
            bool persian = ContainsPersian(message);
            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 520,
                Margin = new Thickness(80, 0, 0, 16)
            };
            container.Children.Add(new TextBlock
            {
                Text = $"YOU  ·  {DateTime.Now:HH:mm}",
                Foreground = ThemeBrush("AppMutedTextBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 2, 5)
            });
            container.Children.Add(new Border
            {
                Background = ThemeBrush("AppAccentSoftBrush"),
                BorderBrush = ThemeBrush("AppAccentBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(15, 11, 15, 11),
                Child = CreateMessageText(message, persian)
            });

            ConversationPanel.Children.Add(container);
            MoodText.Text = persian ? "گوش می‌دهم" : "LISTENING";
            ScrollConversationToEnd();
        }

        private void AddAssistantMessage(
            string message,
            IReadOnlyList<AssistantAction> actions)
        {
            string mood = DetermineMood(message, actions);
            MoodText.Text = mood;

            var row = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 560,
                Margin = new Thickness(0, 0, 45, 18)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Border avatar = CreateAvatar(36);
            Grid.SetColumn(avatar, 0);
            row.Children.Add(avatar);

            var content = new StackPanel
            {
                MaxWidth = 500
            };
            Grid.SetColumn(content, 1);
            content.Children.Add(new TextBlock
            {
                Text = $"{_speakerName.ToUpperInvariant()}  ·  {mood}  ·  {DateTime.Now:HH:mm}",
                Foreground = ThemeBrush("AppAccentBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 9,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 5)
            });
            content.Children.Add(new Border
            {
                MaxWidth = 500,
                Background = ThemeBrush("AppSurfaceAltBrush"),
                BorderBrush = ThemeBrush("AppBorderBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(15, 11, 15, 11),
                Child = CreateMessageText(message, ContainsPersian(message))
            });

            if (actions.Count > 0)
            {
                var actionPanel = new WrapPanel
                {
                    Margin = new Thickness(0, 10, 0, 0)
                };
                foreach (AssistantAction action in actions)
                {
                    var button = new Button
                    {
                        Content = ActionLabel(action),
                        Tag = action,
                        Margin = new Thickness(0, 0, 8, 7),
                        Style = (Style)FindResource("AssistantButton")
                    };
                    if (action.IsPrimary)
                    {
                        button.Background = ThemeBrush("AppAccentSoftBrush");
                        button.BorderBrush = ThemeBrush("AppAccentBrush");
                    }
                    button.Click += ActionButton_Click;
                    actionPanel.Children.Add(button);
                }
                content.Children.Add(actionPanel);
            }

            row.Children.Add(content);
            ConversationPanel.Children.Add(row);
            ScrollConversationToEnd();
        }

        private async void ActionButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isBusy ||
                sender is not Button button ||
                button.Tag is not AssistantAction action)
                return;

            bool persian = ContainsPersian(action.Label);
            button.IsEnabled = false;
            SetBusy(true, persian);

            if (action.Kind == AssistantActionKind.InstallPackage)
            {
                AddAssistantMessage(
                    persian
                        ? "دارم بسته معتبر را با WinGet نصب می‌کنم..."
                        : "Installing the verified package with WinGet...",
                    []);
            }

            try
            {
                AssistantReply reply =
                    await ConversationalAssistantService.Instance.ExecuteActionAsync(
                        action,
                        _characterName,
                        persian);
                AddAssistantMessage(reply.Message, reply.Actions);
                _history.Add(new ConversationTurn("assistant", reply.Message));
                TrimHistory();
                ConversationMemoryStore.Save(_characterName, _history);
                UpdateMemoryStatus();
            }
            catch (Exception ex)
            {
                AddAssistantMessage(
                    persian
                        ? $"اینجا با خیال راحت متوقف شدم: {ex.Message}"
                        : $"I stopped safely: {ex.Message}",
                    []);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void RefreshQuickPrompts(
            string latestPrompt = "",
            string latestReply = "")
        {
            var prompts = new List<string>();
            string combined = $"{latestPrompt} {latestReply}";

            if (ContainsAny(combined, "music", "song", "track", "آهنگ", "موسیقی"))
                prompts.Add(_persona.MusicPrompt);
            else if (ContainsAny(combined, "code", "project", "bug", "برنامه", "کد"))
                prompts.Add(_persona.WorkPrompt);
            else
                prompts.Add(_persona.PersonalPrompt);

            if (!string.IsNullOrWhiteSpace(_visibleContext))
                prompts.Add(_persona.ContextPrompt);

            prompts.AddRange(_persona.DefaultPrompts);

            QuickPromptPanel.Children.Clear();
            foreach (string prompt in prompts
                         .Where(value => !string.IsNullOrWhiteSpace(value))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .Take(4))
            {
                var button = new Button
                {
                    Content = prompt,
                    Tag = prompt,
                    Style = (Style)FindResource("PromptChip"),
                    FlowDirection = ContainsPersian(prompt)
                        ? FlowDirection.RightToLeft
                        : FlowDirection.LeftToRight
                };
                button.Click += QuickPrompt_Click;
                QuickPromptPanel.Children.Add(button);
            }
        }

        private void SetBusy(bool busy, bool persian = false)
        {
            _isBusy = busy;
            _thinkingInPersian = persian;
            SendButton.IsEnabled = !busy;
            PromptTextBox.IsEnabled = !busy;
            QuickPromptPanel.IsEnabled = !busy;
            ClearMemoryButton.IsEnabled = !busy;

            if (busy)
            {
                _typingFrame = 1;
                TypingText.Text = $"{_persona.ThinkingLine(persian)}.";
                TypingIndicator.Visibility = Visibility.Visible;
                MoodText.Text = persian ? "در حال فکر" : "THINKING";
                _typingTimer.Start();
                ScrollConversationToEnd();
            }
            else
            {
                _typingTimer.Stop();
                TypingIndicator.Visibility = Visibility.Collapsed;
            }
        }

        private TextBlock CreateMessageText(string message, bool persian) =>
            new()
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("AppTextBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                LineHeight = 21,
                FlowDirection = persian
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight,
                TextAlignment = persian
                    ? TextAlignment.Right
                    : TextAlignment.Left
            };

        private Border CreateAvatar(double size) =>
            new()
            {
                Width = size,
                Height = size,
                Margin = new Thickness(0, 16, 8, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Background = ThemeBrush("AppAccentSoftBrush"),
                BorderBrush = ThemeBrush("AppAccentBrush"),
                BorderThickness = new Thickness(1),
                Child = new Image
                {
                    Source = _portraitSource,
                    Width = size - 4,
                    Height = size - 4,
                    Stretch = Stretch.Uniform
                }
            };

        private Brush ThemeBrush(string resourceKey) =>
            (Brush)FindResource(resourceKey);

        private void UpdateMemoryStatus()
        {
            int exchanges = _history.Count(turn =>
                string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase));
            MemoryStatusText.Text = exchanges == 0
                ? "Fresh conversation"
                : $"Local memory · {exchanges} exchange{(exchanges == 1 ? "" : "s")}";
        }

        private void TrimHistory()
        {
            const int maximumTurns = 24;
            if (_history.Count > maximumTurns)
                _history.RemoveRange(0, _history.Count - maximumTurns);
        }

        private void ClearMemoryButton_Click(object sender, RoutedEventArgs e)
        {
            DateTime now = DateTime.UtcNow;
            if (!_clearMemoryArmed || now > _clearMemoryArmedUntil)
            {
                _clearMemoryArmed = true;
                _clearMemoryArmedUntil = now.AddSeconds(5);
                ClearMemoryText.Text = "Click again to forget";
                MoodText.Text = "CONFIRM";
                return;
            }

            _history.Clear();
            ConversationMemoryStore.Clear(_characterName);
            ConversationPanel.Children.Clear();
            ResetClearMemoryConfirmation();
            UpdateMemoryStatus();
            RefreshQuickPrompts();
            AddAssistantMessage(_persona.FreshStart, []);
            PromptTextBox.Focus();
        }

        private void ResetClearMemoryConfirmation()
        {
            _clearMemoryArmed = false;
            ClearMemoryText.Text = "New chat";
        }

        private void ScrollConversationToEnd() =>
            Dispatcher.BeginInvoke(
                new Action(() => ConversationScrollViewer.ScrollToEnd()),
                DispatcherPriority.Background);

        private void PromptTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter &&
                Keyboard.Modifiers != ModifierKeys.Shift)
            {
                e.Handled = true;
                _ = SendCurrentPromptAsync();
            }
        }

        private void PromptTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            bool persian = IsMostlyPersian(PromptTextBox.Text);
            PromptTextBox.FlowDirection = persian
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            PromptPlaceholder.Visibility = PromptTextBox.Text.Length == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            PromptPlaceholder.FlowDirection = persian
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }

        private static ImageSource LoadPortrait(string resourcePath)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(
                $"pack://application:,,,/{resourcePath}",
                UriKind.Absolute);
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze();
            return image;
        }

        private static string ActionLabel(AssistantAction action) =>
            action.Kind switch
            {
                AssistantActionKind.OpenUrl => $"↗  {action.Label}",
                AssistantActionKind.ConfirmInstall => $"↓  {action.Label}",
                AssistantActionKind.InstallPackage => $"✓  {action.Label}",
                _ => action.Label
            };

        private static string DetermineMood(
            string message,
            IReadOnlyList<AssistantAction> actions)
        {
            if (ContainsPersian(message))
            {
                if (actions.Count > 0) return "آماده‌ام";
                if (message.Contains('؟')) return "کنجکاوم";
                return "همراهتم";
            }

            if (actions.Count > 0) return "READY TO ACT";
            if (message.Contains('?')) return "CURIOUS";
            if (ContainsAny(message, "great", "glad", "nice", "love", "win"))
                return "BRIGHT";
            if (ContainsAny(message, "think", "consider", "perhaps", "maybe"))
                return "THOUGHTFUL";
            return "ENGAGED";
        }

        private static bool ContainsAny(string value, params string[] needles) =>
            needles.Any(needle =>
                value.Contains(needle, StringComparison.OrdinalIgnoreCase));

        private static string Shorten(string value, int maximumLength) =>
            value.Length <= maximumLength
                ? value
                : $"{value[..(maximumLength - 1)]}…";

        private static bool IsMostlyPersian(string text)
        {
            int persian = text.Count(
                character => character is >= '\u0600' and <= '\u06FF');
            int latin = text.Count(character => character is >= 'A' and <= 'z');
            return persian > latin;
        }

        private static bool ContainsPersian(string text) =>
            text.Any(character => character is >= '\u0600' and <= '\u06FF');

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private sealed record ConversationPersona(
            string Role,
            string Tagline,
            string PortraitPath,
            string Greeting,
            string WelcomeBack,
            string FreshStart,
            string PersonalPrompt,
            string ContextPrompt,
            string MusicPrompt,
            string WorkPrompt,
            string[] DefaultPrompts,
            string EnglishThinkingLine,
            string PersianThinkingLine)
        {
            public string ThinkingLine(bool persian) =>
                persian ? PersianThinkingLine : EnglishThinkingLine;

            public static ConversationPersona For(
                string characterName,
                string speakerName) =>
                characterName switch
                {
                    "Pink Monster" => new ConversationPersona(
                        "CURIOUS DESKTOP CREATURE",
                        "Playful, emotionally expressive, and always looking for the interesting detail.",
                        "Resources/Characters/PinkMonster/Pink_Monster.png",
                        "Hey—you found my little corner of the screen! Talk to me normally. Tell me what is on your mind, show me what you are working on, or ask me to open something.",
                        "You’re back! I kept the thread of our last conversation tucked somewhere safe. Where should we pick it up?",
                        "Fresh page! No old assumptions. What should this version of our conversation be about?",
                        "What are you curious about today?",
                        "What catches your eye on this screen?",
                        "What kind of world does this music create?",
                        "Help me make this project more interesting",
                        ["Surprise me with a question", "Open Steam", "حالت امروز چطوره؟"],
                        "Pip is chasing that thought",
                        "پیپ دنبال این فکر می‌دود"),

                    "Owlet Monster" => new ConversationPersona(
                        "PERCEPTIVE PIXEL SCHOLAR",
                        "Calm, precise, quietly witty, and interested in how your ideas fit together.",
                        "Resources/Characters/OwletMonster/Owlet_Monster.png",
                        "Welcome. You need not turn every thought into a command here. We can examine an idea, solve a problem, or simply follow a conversation wherever it leads.",
                        "Welcome back. I retained the shape of our earlier discussion, not merely its final sentence. What deserves our attention now?",
                        "A clean slate is intellectually healthy from time to time. What shall we examine first?",
                        "Help me think through an idea",
                        "What do you infer from this screen?",
                        "What do you notice about this track?",
                        "Help me reason through this project",
                        ["Teach me something unexpected", "Open Visual Studio Code", "به نظرت امروز روی چی تمرکز کنم؟"],
                        "Professor Owlet is considering the evidence",
                        "پروفسور اولت شواهد را بررسی می‌کند"),

                    _ => new ConversationPersona(
                        "STRAIGHT-TALKING SIDEKICK",
                        "Direct, practical, dryly funny, and warmer than he first lets on.",
                        "Resources/Characters/DudeMonster/Dude_Monster.png",
                        "All right, I’m here. You can skip the assistant voice and talk like a person. Want an honest opinion, practical help, or just some company?",
                        "Back again. Good—I remember the useful parts. What are we dealing with now?",
                        "Clean slate. No baggage. Hit me with the first real thought.",
                        "Give me your honest take",
                        "What am I looking at here?",
                        "Does this track pass the vibe check?",
                        "Help me cut through this project",
                        ["Ask me something real", "Open Steam", "رک و راست نظرت چیه؟"],
                        "Dude is thinking it through",
                        "دود دارد سبک‌سنگینش می‌کند")
                };
        }
    }
}
