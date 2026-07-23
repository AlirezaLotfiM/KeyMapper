using System;
using System.Windows;

namespace KeyMapper
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
            Loaded += OverlayWindow_Loaded;
        }

        private void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
        {
            PositionWindow();
        }

        public void PositionWindow()
        {
            var desktopWorkingArea = SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 25;
            this.Top = desktopWorkingArea.Bottom - this.Height - 25;
        }

        public void ShowBuffer(string text)
        {
            BufferText.Text = string.IsNullOrEmpty(text) ? "..." : text;
            
            if (!this.IsVisible)
            {
                // Position again in case screen resolution or work area changed
                PositionWindow();
                this.Show();
            }
        }

        public void HideBuffer()
        {
            this.Hide();
        }
    }
}
