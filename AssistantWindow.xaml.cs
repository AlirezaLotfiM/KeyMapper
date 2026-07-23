using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyMapper
{
    public partial class AssistantWindow : Window
    {
        private readonly string _characterName;
        private readonly string _speakerName;
        private readonly string _visibleContext;
        private readonly List<ConversationTurn> _history = new();
        private bool _isBusy;

        public AssistantWindow(string characterName, string visibleContext = "")
        {
            InitializeComponent();
            _characterName = characterName;
            _speakerName = PetPersonalities.For(characterName).SpeakerName;
            _visibleContext = visibleContext;
            WindowTitleText.Text = $"Talk to {_speakerName}";

            Loaded += (sender, args) =>
            {
                AddAssistantMessage(InitialGreeting(), []);
                PromptTextBox.Focus();
            };
        }

        private string InitialGreeting() => _characterName switch
        {
            "Pink Monster" =>
                "Hi! We can actually talk—not only trade commands. Persian and English both work, and I can still open things when you ask.",
            "Owlet Monster" =>
                "We may speak naturally in Persian or English. If you ask for a computer action, I shall verify it locally before proceeding.",
            _ =>
                "Talk normally. Commands still work, but you don’t have to phrase everything like one."
        };

        private async void SendButton_Click(object sender, RoutedEventArgs e) =>
            await SendCurrentPromptAsync();

        private async void QuickPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not string prompt) return;
            PromptTextBox.Text = prompt;
            await SendCurrentPromptAsync();
        }

        private async Task SendCurrentPromptAsync()
        {
            if (_isBusy) return;
            string prompt = PromptTextBox.Text.Trim();
            if (prompt.Length == 0) return;

            AddUserMessage(prompt);
            PromptTextBox.Clear();
            SetBusy(true);
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
            }
            catch (Exception ex)
            {
                AddAssistantMessage($"I stopped safely: {ex.Message}", []);
            }
            finally
            {
                SetBusy(false);
                PromptTextBox.Focus();
            }
        }

        private void AddUserMessage(string message)
        {
            var text = CreateMessageText(message);
            var bubble = new Border
            {
                Background = ThemeBrush("AppAccentSoftBrush"),
                BorderBrush = ThemeBrush("AppAccentBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(13, 10, 13, 10),
                Margin = new Thickness(70, 0, 0, 12),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 430,
                Child = text
            };
            ConversationPanel.Children.Add(bubble);
            ScrollConversationToEnd();
        }

        private void AddAssistantMessage(
            string message,
            System.Collections.Generic.IReadOnlyList<AssistantAction> actions)
        {
            var container = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                MaxWidth = 470,
                Margin = new Thickness(0, 0, 50, 14)
            };
            container.Children.Add(new TextBlock
            {
                Text = _speakerName,
                Foreground = ThemeBrush("AppAccentBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(2, 0, 0, 5)
            });

            var bubble = new Border
            {
                Background = ThemeBrush("AppSurfaceAltBrush"),
                BorderBrush = ThemeBrush("AppBorderBrush"),
                BorderThickness = new Thickness(2),
                Padding = new Thickness(13, 10, 13, 10),
                Child = CreateMessageText(message)
            };
            container.Children.Add(bubble);

            if (actions.Count > 0)
            {
                var actionPanel = new WrapPanel
                {
                    Margin = new Thickness(0, 9, 0, 0)
                };
                foreach (AssistantAction action in actions)
                {
                    var button = new Button
                    {
                        Content = action.Label,
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
                container.Children.Add(actionPanel);
            }

            ConversationPanel.Children.Add(container);
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
            SetBusy(true);
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
            }
            catch (Exception ex)
            {
                AddAssistantMessage($"I stopped safely: {ex.Message}", []);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private TextBlock CreateMessageText(string message)
        {
            bool persian = ContainsPersian(message);
            return new TextBlock
            {
                Text = message,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ThemeBrush("AppTextBrush"),
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13,
                LineHeight = 20,
                FlowDirection = persian
                    ? FlowDirection.RightToLeft
                    : FlowDirection.LeftToRight,
                TextAlignment = persian
                    ? TextAlignment.Right
                    : TextAlignment.Left
            };
        }

        private Brush ThemeBrush(string resourceKey) =>
            (Brush)FindResource(resourceKey);

        private void SetBusy(bool busy)
        {
            _isBusy = busy;
            SendButton.IsEnabled = !busy;
            PromptTextBox.IsEnabled = !busy;
            QuickPromptPanel.IsEnabled = !busy;
        }

        private void ScrollConversationToEnd() =>
            Dispatcher.BeginInvoke(
                new Action(() => ConversationScrollViewer.ScrollToEnd()));

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
            PromptTextBox.FlowDirection = IsMostlyPersian(PromptTextBox.Text)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
        }

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
    }
}
