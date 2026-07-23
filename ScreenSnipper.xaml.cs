using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class ScreenSnipper : Window
    {
        private System.Windows.Point _startPoint;
        private bool _isSnipping;
        private readonly Action<Bitmap> _onSnipCompleted;

        public ScreenSnipper(Action<Bitmap> onSnipCompleted)
        {
            InitializeComponent();
            _onSnipCompleted = onSnipCompleted;
        }

        public static void StartSnipping(Action<Bitmap> onSnipCompleted)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                var snipper = new ScreenSnipper(onSnipCompleted);
                snipper.ShowDialog();
            });
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _isSnipping = true;
                _startPoint = e.GetPosition(SnipCanvas);

                Canvas.SetLeft(SelectionRectangle, _startPoint.X);
                Canvas.SetTop(SelectionRectangle, _startPoint.Y);
                SelectionRectangle.Width = 0;
                SelectionRectangle.Height = 0;
                SelectionRectangle.Visibility = Visibility.Visible;
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isSnipping)
            {
                System.Windows.Point currentPoint = e.GetPosition(SnipCanvas);

                double x = Math.Min(_startPoint.X, currentPoint.X);
                double y = Math.Min(_startPoint.Y, currentPoint.Y);
                double width = Math.Abs(_startPoint.X - currentPoint.X);
                double height = Math.Abs(_startPoint.Y - currentPoint.Y);

                Canvas.SetLeft(SelectionRectangle, x);
                Canvas.SetTop(SelectionRectangle, y);
                SelectionRectangle.Width = width;
                SelectionRectangle.Height = height;
            }
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isSnipping)
            {
                _isSnipping = false;
                SelectionRectangle.Visibility = Visibility.Collapsed;

                double x = Canvas.GetLeft(SelectionRectangle);
                double y = Canvas.GetTop(SelectionRectangle);
                double width = SelectionRectangle.Width;
                double height = SelectionRectangle.Height;

                Hide();

                if (width > 5 && height > 5)
                {
                    Bitmap capturedBitmap = CaptureScreenRegion((int)x, (int)y, (int)width, (int)height);
                    _onSnipCompleted?.Invoke(capturedBitmap);
                }

                Close();
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        }

        private Bitmap CaptureScreenRegion(int x, int y, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(x, y, 0, 0, new System.Drawing.Size(width, height), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
    }
}
