using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class TranslatorWindow : Window
    {
        private readonly DispatcherTimer _liveTranslationTimer;
        private CancellationTokenSource? _translationCancellation;
        private string _targetLanguage = "en";
        private string _detectedSourceLanguage = string.Empty;
        private bool _isLoaded;
        private bool _isLoadingSettings;
        private bool _isApplyingDetectedLanguage;

        public TranslatorWindow(string initialText)
        {
            InitializeComponent();

            _liveTranslationTimer = new DispatcherTimer();
            _liveTranslationTimer.Tick += async (s, e) =>
            {
                _liveTranslationTimer.Stop();
                await TranslateCurrentTextAsync();
            };

            Loaded += (s, e) =>
            {
                _isLoadingSettings = true;
                AppSettings settings = ConfigManager.Load();
                EndpointTextBox.Text = string.IsNullOrWhiteSpace(settings.LibreTranslateEndpoint)
                    ? "http://localhost:5000"
                    : settings.LibreTranslateEndpoint;
                ApiKeyBox.Password = settings.LibreTranslateApiKey;
                SourceTextBox.Text = initialText;
                _isLoadingSettings = false;
                _isLoaded = true;

                SourceTextBox.Focus();
                SourceTextBox.SelectAll();
                ScheduleLiveTranslation(initialText.Length > 0);
            };

            Closed += (s, e) =>
            {
                _liveTranslationTimer.Stop();
                _translationCancellation?.Cancel();
                _translationCancellation?.Dispose();
            };
        }

        private void SourceTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoaded)
            {
                ResetDetectedLanguage();
                ScheduleLiveTranslation();
            }
        }

        private void TargetLanguage_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton radio && radio.Tag is string language)
            {
                _targetLanguage = language;
                if (_isLoaded && !_isApplyingDetectedLanguage)
                    ScheduleLiveTranslation(true);
            }
        }

        private void ScheduleLiveTranslation(bool immediately = false)
        {
            _liveTranslationTimer.Stop();
            _translationCancellation?.Cancel();

            if (string.IsNullOrWhiteSpace(SourceTextBox.Text))
            {
                ResultTextBox.Text = string.Empty;
                StatusText.Text = "Waiting for text";
                return;
            }

            if (immediately)
            {
                _ = TranslateCurrentTextAsync();
                return;
            }

            StatusText.Text = "Waiting for you to finish typing...";
            // A short internal debounce keeps the interface live without exposing a
            // technical timing control to the user.
            _liveTranslationTimer.Interval = TimeSpan.FromMilliseconds(420);
            _liveTranslationTimer.Start();
        }

        private async System.Threading.Tasks.Task TranslateCurrentTextAsync()
        {
            string source = SourceTextBox.Text.Trim();
            if (source.Length == 0) return;

            string endpoint = string.IsNullOrWhiteSpace(EndpointTextBox.Text)
                ? "http://localhost:5000"
                : EndpointTextBox.Text.Trim();
            string targetAtStart = _targetLanguage;

            SaveAdvancedSettings();
            _translationCancellation?.Dispose();
            _translationCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = _translationCancellation.Token;

            StatusText.Text = "Translating...";
            try
            {
                TranslationResult result = await LibreTranslateService.Instance.TranslateAsync(
                    source,
                    targetAtStart,
                    endpoint,
                    ApiKeyBox.Password.Trim(),
                    cancellationToken);

                if (cancellationToken.IsCancellationRequested ||
                    source != SourceTextBox.Text.Trim() ||
                    targetAtStart != _targetLanguage)
                    return;

                string detected = NormalizeSupportedLanguage(result.DetectedLanguage);
                if (detected.Length > 0)
                {
                    _detectedSourceLanguage = detected;
                    if (detected == targetAtStart)
                    {
                        string betterTarget = ChooseAlternateTarget(detected);
                        SelectTargetWithoutRetriggering(betterTarget);
                        UpdateLanguageChoices(detected);
                        StatusText.Text = $"Detected {LanguageName(detected)} — switching to {LanguageName(betterTarget)}";
                        await TranslateCurrentTextAsync();
                        return;
                    }

                    UpdateLanguageChoices(detected);
                }

                ResultTextBox.Text = result.TranslatedText;
                StatusText.Text = detected.Length > 0
                    ? $"{LanguageName(detected)} → {LanguageName(targetAtStart)}"
                    : $"Translated to {LanguageName(targetAtStart)}";
            }
            catch (OperationCanceledException)
            {
                // A newer edit or language choice superseded this request.
            }
            catch (Exception ex)
            {
                StatusText.Text = FriendlyError(ex);
            }
        }

        private static string FriendlyError(Exception exception)
        {
            if (exception is System.Net.Http.HttpRequestException)
                return "Translation server is offline. Open SETTINGS to change it.";
            if (exception is System.Threading.Tasks.TaskCanceledException)
                return "The translation server took too long to answer.";
            return exception.Message;
        }

        private void AdvancedSetting_Changed(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded || _isLoadingSettings) return;
            SaveAdvancedSettings();
            ScheduleLiveTranslation();
        }

        private void SaveAdvancedSettings()
        {
            if (_isLoadingSettings) return;
            AppSettings settings = ConfigManager.Load();
            settings.LibreTranslateEndpoint = string.IsNullOrWhiteSpace(EndpointTextBox.Text)
                ? "http://localhost:5000"
                : EndpointTextBox.Text.Trim();
            settings.LibreTranslateApiKey = ApiKeyBox.Password.Trim();
            ConfigManager.Save(settings);
        }

        private void ResetDetectedLanguage()
        {
            _detectedSourceLanguage = string.Empty;
            EnglishTarget.IsEnabled = true;
            GermanTarget.IsEnabled = true;
            PersianTarget.IsEnabled = true;
            EnglishTarget.ToolTip = null;
            GermanTarget.ToolTip = null;
            PersianTarget.ToolTip = null;
        }

        private void UpdateLanguageChoices(string sourceLanguage)
        {
            ResetDetectedLanguage();
            _detectedSourceLanguage = sourceLanguage;
            RadioButton? sourceButton = ButtonForLanguage(sourceLanguage);
            if (sourceButton != null && sourceButton.IsChecked != true)
            {
                sourceButton.IsEnabled = false;
                sourceButton.ToolTip = "This is the detected source language.";
            }
        }

        private void SelectTargetWithoutRetriggering(string language)
        {
            _isApplyingDetectedLanguage = true;
            _targetLanguage = language;
            RadioButton? button = ButtonForLanguage(language);
            if (button != null) button.IsChecked = true;
            _isApplyingDetectedLanguage = false;
        }

        private RadioButton? ButtonForLanguage(string language) => language switch
        {
            "en" => EnglishTarget,
            "de" => GermanTarget,
            "fa" => PersianTarget,
            _ => null
        };

        private static string ChooseAlternateTarget(string sourceLanguage) => sourceLanguage switch
        {
            "en" => "fa",
            "fa" => "en",
            "de" => "en",
            _ => "en"
        };

        private static string NormalizeSupportedLanguage(string language)
        {
            string normalized = language.Trim().ToLowerInvariant();
            return normalized is "en" or "de" or "fa" ? normalized : string.Empty;
        }

        private static string LanguageName(string language) => language switch
        {
            "en" => "English",
            "de" => "German",
            "fa" => "Persian",
            _ => "the selected language"
        };

        private void ToggleSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            bool showSettings = ServerSettingsPanel.Visibility != Visibility.Visible;
            ServerSettingsPanel.Visibility = showSettings
                ? Visibility.Visible
                : Visibility.Collapsed;

            Dispatcher.BeginInvoke(() =>
            {
                if (showSettings)
                    BodyScrollViewer.ScrollToEnd();
                else
                    BodyScrollViewer.ScrollToHome();
            }, DispatcherPriority.Loaded);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ResultTextBox.Text)) return;
            Clipboard.SetText(ResultTextBox.Text);
            StatusText.Text = "Copied to clipboard";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) DragMove();
        }
    }
}
