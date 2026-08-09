using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace KeyMapper
{
    public partial class StickyNoteConfirmDialog : Window
    {
        public StickyNoteConfirmDialog(string noteTitle, string colorTheme, Window? owner = null)
        {
            InitializeComponent();

            if (owner != null)
            {
                Owner = owner;
                Topmost = owner.Topmost;
            }

            string safeTitle = string.IsNullOrWhiteSpace(noteTitle) ? "this note" : noteTitle.Trim();
            PromptText.Text = $"Delete \"{safeTitle}\" from your desktop notes?\nThe note content and its attachments will be removed.";
            ApplyTheme(colorTheme);
        }

        private void HeaderPanel_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                DialogResult = true;
                e.Handled = true;
            }
        }

        private void ApplyTheme(string themeName)
        {
            Color background = ResolveBackground(themeName);
            bool isDark = GetRelativeLuminance(background) < 0.52;
            Color border = Mix(background, isDark ? Colors.White : Colors.Black, isDark ? 0.28 : 0.18);
            Color accent = isDark ? Color.FromRgb(0x64, 0xD8, 0xE3) : Color.FromRgb(0x31, 0x8D, 0x99);
            Color danger = isDark ? Color.FromRgb(0xF3, 0x6F, 0x87) : Color.FromRgb(0xD6, 0x4B, 0x63);
            Brush primaryText = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x26, 0x38, 0x4F));
            Brush secondaryText = isDark ? new SolidColorBrush(Color.FromRgb(0xD7, 0xCF, 0xE3)) : new SolidColorBrush(Color.FromRgb(0x68, 0x73, 0x84));

            DialogCard.Background = new SolidColorBrush(background);
            DialogCard.BorderBrush = new SolidColorBrush(border);
            HeaderPanel.Background = new SolidColorBrush(Color.FromArgb(0x16, accent.R, accent.G, accent.B));
            IconBadge.Background = new SolidColorBrush(Color.FromArgb(0x28, accent.R, accent.G, accent.B));
            DialogTitle.Foreground = primaryText;
            DialogSubtitle.Foreground = secondaryText;
            PromptText.Foreground = primaryText;

            CloseButton.Foreground = secondaryText;
            CloseButton.Background = Brushes.Transparent;
            CloseButton.BorderBrush = Brushes.Transparent;

            CancelButton.Foreground = primaryText;
            CancelButton.Background = new SolidColorBrush(Color.FromArgb(0x18, accent.R, accent.G, accent.B));
            CancelButton.BorderBrush = new SolidColorBrush(Color.FromArgb(0x80, accent.R, accent.G, accent.B));

            DeleteButton.Foreground = Brushes.White;
            DeleteButton.Background = new SolidColorBrush(danger);
            DeleteButton.BorderBrush = new SolidColorBrush(danger);
        }

        private static Color ResolveBackground(string themeName)
        {
            return themeName switch
            {
                "Pastel Pink" => Color.FromRgb(0xF8, 0xBB, 0xD0),
                "Soft Mint" => Color.FromRgb(0xA5, 0xD6, 0xA7),
                "Sky Blue" => Color.FromRgb(0x80, 0xDE, 0xEA),
                "Lavender" => Color.FromRgb(0xE1, 0xBE, 0xE7),
                "Dark Carbon" => Color.FromRgb(0x2C, 0x2C, 0x2C),
                "Warm Cream" => Color.FromRgb(0xFF, 0xF8, 0xE7),
                "Coral" => Color.FromRgb(0xFF, 0x8A, 0x80),
                "Peach" => Color.FromRgb(0xFF, 0xD1, 0x80),
                "Sage" => Color.FromRgb(0xC8, 0xE6, 0xC9),
                "Teal" => Color.FromRgb(0xB2, 0xDF, 0xDB),
                "Indigo" => Color.FromRgb(0xC5, 0xCA, 0xE9),
                "Plum" => Color.FromRgb(0xD1, 0xC4, 0xE9),
                "Mocha" => Color.FromRgb(0xD7, 0xCC, 0xC8),
                "Cyber Neon" => Color.FromRgb(0x18, 0x10, 0x28),
                "Sunset Purple" => Color.FromRgb(0x2A, 0x1B, 0x3D),
                _ => ParseCustomOrDefault(themeName)
            };
        }

        private static Color ParseCustomOrDefault(string themeName)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(themeName) && themeName.StartsWith("#"))
                {
                    return (Color)ColorConverter.ConvertFromString(themeName);
                }
            }
            catch
            {
                // Fall through to the default note color.
            }

            return Color.FromRgb(0xFF, 0xF5, 0x9D);
        }

        private static Color Mix(Color source, Color target, double amount)
        {
            byte Blend(byte a, byte b) => (byte)Math.Clamp(a + ((b - a) * amount), 0, 255);
            return Color.FromRgb(Blend(source.R, target.R), Blend(source.G, target.G), Blend(source.B, target.B));
        }

        private static double GetRelativeLuminance(Color color)
        {
            static double Linearize(byte channel)
            {
                double value = channel / 255.0;
                return value <= 0.03928
                    ? value / 12.92
                    : Math.Pow((value + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linearize(color.R)
                 + 0.7152 * Linearize(color.G)
                 + 0.0722 * Linearize(color.B);
        }
    }
}
