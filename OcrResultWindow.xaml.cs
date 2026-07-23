using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class OcrResultWindow : Window
    {
        public OcrResultWindow(OcrRecognitionResult result)
        {
            InitializeComponent();
            RecognizedTextBox.Text = result.Text;
            RecognizedTextBox.FlowDirection = IsMostlyPersian(result.Text)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;

            string confidence = result.Confidence > 0
                ? $" · {Math.Round(result.Confidence * 100)}% confidence"
                : string.Empty;
            DetailsText.Text = $"{result.LanguageSummary} · {result.EngineName}{confidence}";

            Loaded += (sender, args) =>
            {
                RecognizedTextBox.Focus();
                RecognizedTextBox.CaretIndex = RecognizedTextBox.Text.Length;
            };
        }

        private static bool IsMostlyPersian(string text)
        {
            int persianCount = text.Count(
                character => character is >= '\u0600' and <= '\u06FF');
            int latinCount = text.Count(
                character => character is >= 'A' and <= 'z');
            return persianCount > latinCount;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RecognizedTextBox.Text)) return;
            Clipboard.SetText(RecognizedTextBox.Text);
            ActionStatusText.Text = "Copied to the clipboard.";
        }

        private void TranslateButton_Click(object sender, RoutedEventArgs e)
        {
            string text = RecognizedTextBox.Text.Trim();
            if (text.Length == 0)
            {
                ActionStatusText.Text = "There is no text to translate.";
                return;
            }

            var translator = new TranslatorWindow(text)
            {
                Owner = this
            };
            translator.Show();
            ActionStatusText.Text = "Opened in the live translator.";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
    }
}
