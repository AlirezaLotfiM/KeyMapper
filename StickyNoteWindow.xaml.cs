using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace KeyMapper
{
    public partial class StickyNoteWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0100;
        private const uint SWP_NOACTIVATE = 0x0010;

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        public StickyNoteModel Note { get; }
        private readonly AudioRecorderService _audioRecorder;
        private bool _isUpdatingUi;
        private bool _isEditMode;
        private bool _isResizing;
        private bool _toolbarDensityInitialized;
        private bool _useCompactToolbar;
        private bool _useCondensedCompactToolbar;
        private bool _useMinimalCompactToolbar;
        private bool _isSynchronizingFontSelectors;
        private readonly DispatcherTimer _sidebarHideTimer;

        public StickyNoteWindow(StickyNoteModel note)
        {
            _isUpdatingUi = true;
            Note = note;
            InitializeComponent();
            _audioRecorder = new AudioRecorderService();

            _sidebarHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            _sidebarHideTimer.Tick += (s, e) =>
            {
                _sidebarHideTimer.Stop();
                if (!_isEditMode && !ThemePopup.IsOpen && !AiPopup.IsOpen && !TranslatePopup.IsOpen && !TablePopup.IsOpen && !TextColorPopup.IsOpen && !HighlightPopup.IsOpen && !ListPopup.IsOpen && !ToolbarMorePopup.IsOpen)
                {
                    SidebarMenuBorder.Opacity = 0.0;
                    FooterBar.Opacity = 0.0;
                }
            };

            _audioRecorder.PlaybackProgressChanged += OnPlaybackProgress;
            _audioRecorder.PlaybackStopped += OnPlaybackStopped;
            _audioRecorder.RecordingTimeUpdated += OnRecordingTimeUpdated;

            Loaded += StickyNoteWindow_Loaded;
            Deactivated += StickyNoteWindow_Deactivated;
            LocationChanged += StickyNoteWindow_LocationChanged;
            SizeChanged += StickyNoteWindow_SizeChanged;

            // Handle Clipboard Image Paste
            RichEditor.AddHandler(DataObject.PastingEvent, new DataObjectPastingEventHandler(OnRichEditorPaste));
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);

                HwndSource source = HwndSource.FromHwnd(hwnd);
                source?.AddHook(WndProc);
            }
            catch { }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_MINIMIZE = 0xF020;

            if (msg == WM_SYSCOMMAND)
            {
                int command = wParam.ToInt32() & 0xFFF0;
                if (command == SC_MINIMIZE && !Note.IsPinned)
                {
                    handled = true;
                    return IntPtr.Zero;
                }
            }
            return IntPtr.Zero;
        }

        private void StickyNoteWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyNoteModelToUi();
            UpdateWindowZOrder();
        }

        private void StickyNoteWindow_Deactivated(object? sender, EventArgs e)
        {
            // Do not interrupt an active resize drag; Z-order changes corrupt size.
            if (_isResizing) return;

            if (ThemePopup.IsOpen && !ThemePopupCard.IsMouseOver)
            {
                ThemePopup.IsOpen = false;
            }

            if (_isEditMode && !Note.IsToolbarPinned)
            {
                ExitEditMode();
            }

            if (!Note.IsPinned)
            {
                SendToDesktopBottom();
            }
            SaveNoteState();
        }

        private void StickyNoteWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (_isUpdatingUi || WindowState != WindowState.Normal) return;
            Note.Left = Left;
            Note.Top = Top;
            SaveNoteState();
        }

        private void StickyNoteWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyResponsiveToolbarLayout();
            if (_isUpdatingUi || WindowState != WindowState.Normal || Note.IsCollapsed) return;
            Note.Width = Width;
            Note.Height = Height;
            SaveNoteState();
        }

        private void NoteCardBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (NoteCardBorder == null ||
                NoteCardBorder.ActualWidth <= 0 ||
                NoteCardBorder.ActualHeight <= 0)
            {
                return;
            }

            double radius = NoteCardBorder.CornerRadius.TopLeft;
            NoteCardBorder.Clip = new RectangleGeometry(
                new Rect(
                    0,
                    0,
                    NoteCardBorder.ActualWidth,
                    NoteCardBorder.ActualHeight),
                radius,
                radius);
            ApplyResponsiveToolbarLayout(NoteCardBorder.ActualWidth);
        }

        private void ApplyNoteModelToUi()
        {
            _isUpdatingUi = true;

            Left = Note.Left;
            Top = Note.Top;
            Width = Note.Width > 0 ? Note.Width : 240;

            if (Note.IsCollapsed)
            {
                MinHeight = 90;
                Height = 90;
            }
            else
            {
                MinHeight = 90;
                Height = Note.Height > 0 ? Note.Height : 240;
            }

            TitleTextBox.Text = Note.Title;
            ApplyColorTheme(Note.ColorTheme);
            SetPinnedState(Note.IsPinned);
            ApplyColumnLayout(Note.ColumnCount);

            LoadDocumentContent();
            ApplyToolbarLayout();

            if (!string.IsNullOrEmpty(Note.AudioMemoPath) && File.Exists(Note.AudioMemoPath))
            {
                AudioBarRow.Height = GridLength.Auto;
                AudioBar.Visibility = Visibility.Visible;
                AudioStatusText.Text = $"Voice Memo ({Note.AudioDurationSeconds:0.#}s)";
            }
            else
            {
                AudioBar.Visibility = Visibility.Collapsed;
            }

            if (Note.IsCollapsed)
            {
                CollapseNote(true);
            }

            _isUpdatingUi = false;
            UpdateWordCount();
        }

        private void LoadDocumentContent()
        {
            try
            {
                RichEditor.Document.Blocks.Clear();
                if (!string.IsNullOrEmpty(Note.RtfContent))
                {
                    TextRange range = new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd);
                    using (MemoryStream ms = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(Note.RtfContent)))
                    {
                        range.Load(ms, DataFormats.Rtf);
                    }
                }
                else if (!string.IsNullOrEmpty(Note.PlainTextContent))
                {
                    // Split on newlines so each line becomes its own Paragraph.
                    // a single Run containing \n does NOT render as a line break in WPF RichTextBox.
                    var lines = Note.PlainTextContent.Split('\n');
                    foreach (var line in lines)
                    {
                        RichEditor.Document.Blocks.Add(new Paragraph(new Run(line.TrimEnd('\r'))));
                    }
                }
            }
            catch
            {
                RichEditor.Document.Blocks.Clear();
                var fallbackLines = (Note.PlainTextContent ?? "").Split('\n');
                foreach (var line in fallbackLines)
                    RichEditor.Document.Blocks.Add(new Paragraph(new Run(line.TrimEnd('\r'))));
            }
        }

        public void SaveNoteState()
        {
            if (_isUpdatingUi || Note == null || TitleTextBox == null || RichEditor?.Document == null) return;

            Note.Title = TitleTextBox.Text;
            Note.Left = Left;
            Note.Top = Top;
            Note.Width = Width;
            Note.Height = Height;

            try
            {
                TextRange range = new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd);
                using (MemoryStream ms = new MemoryStream())
                {
                    range.Save(ms, DataFormats.Rtf);
                    Note.RtfContent = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                }
                Note.PlainTextContent = range.Text;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save document content: {ex.Message}");
            }

            StickyNoteManager.Instance.UpdateNoteModel(Note);
        }

        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOZORDER = 0x0004;

        private void UpdateWindowZOrder()
        {
            if (Note.IsPinned)
            {
                SendToTopmostWindow();
            }
            else
            {
                SendToDesktopBottom();
            }
        }

        public void SendToDesktopBottom()
        {
            try
            {
                Topmost = false;
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd == IntPtr.Zero) return;

                SetParent(hwnd, IntPtr.Zero);
                // Strip HWND_TOPMOST style before HWND_BOTTOM can take effect
                //SetWindowPos(hwnd, HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                //SetWindowPos(hwnd, HWND_BOTTOM,    0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                // NOTE: Do NOT call SetForegroundWindow(desktopHwnd) here.
                // It triggers a second WPF Deactivated event that causes a layout
                // recalculation and resets the window size before SaveNoteState captures it.
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SendToDesktopBottom error: {ex.Message}");
            }
        }

        private void SendToTopmostWindow()
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    SetParent(hwnd, IntPtr.Zero);
                    Topmost = true;
                    //SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SendToTopmostWindow error: {ex.Message}");
            }
        }

        public void SetPinnedState(bool pinned)
        {
            Note.IsPinned = pinned;
            if (PinIconSlash != null)
            {
                // When pinned, show UNPIN icon (with slash) to unpin. When unpinned, show PIN icon (clean) to pin.
                PinIconSlash.Visibility = pinned ? Visibility.Visible : Visibility.Collapsed;
            }

            PinBadgeLabel.Text = pinned ? "Pinned" : "Desktop";
            PinButton.ToolTip = pinned ? "Unpin Note (Stick to Desktop)" : "Pin Note (Always On Top)";
            UpdateWindowZOrder();
            SaveNoteState();
        }

        private void ApplyColorTheme(string themeName)
        {
            Note.ColorTheme = themeName;
            Brush bgBrush;
            Brush borderBrush;
            Brush textBrush = Brushes.Black;
            bool isDark = false;

            if (themeName.StartsWith("#"))
            {
                try
                {
                    Color customColor = (Color)ColorConverter.ConvertFromString(themeName);
                    bgBrush = new SolidColorBrush(customColor);
                    borderBrush = new SolidColorBrush(customColor);
                    isDark = GetRelativeLuminance(customColor) < 0.52;
                    textBrush = isDark ? Brushes.White : Brushes.Black;
                }
                catch
                {
                    bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF59D"));
                    borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF59D"));
                }
            }
            else
            {
                switch (themeName)
                {
                    case "Pastel Pink":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8BBD0"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F48FB1"));
                        break;
                    case "Soft Mint":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5D6A7"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#81C784"));
                        break;
                    case "Sky Blue":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80DEEA"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#4DD0E1"));
                        break;
                    case "Lavender":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#E1BEE7"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CE93D8"));
                        break;
                    case "Dark Carbon":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2C2C2C"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#444444"));
                        textBrush = Brushes.White;
                        isDark = true;
                        break;
                    case "Warm Cream":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF8E7"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFE0B2"));
                        break;
                    case "Coral":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF8A80"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF5252"));
                        break;
                    case "Peach":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD180"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFAB40"));
                        break;
                    case "Sage":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C8E6C9"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5D6A7"));
                        break;
                    case "Teal":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B2DFDB"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#80CBC4"));
                        break;
                    case "Indigo":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#C5CAE9"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FA8DA"));
                        break;
                    case "Plum":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D1C4E9"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B39DDB"));
                        break;
                    case "Mocha":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D7CCC8"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#BCAAA4"));
                        break;
                    case "Cyber Neon":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#181028"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B2263"));
                        textBrush = Brushes.White;
                        isDark = true;
                        break;
                    case "Sunset Purple":
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2A1B3D"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#51324C"));
                        textBrush = Brushes.White;
                        isDark = true;
                        break;
                    default: // Warm Yellow
                        bgBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF59D"));
                        borderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF59D"));
                        break;
                }
            }

            NoteCardBorder.Background = bgBrush;
            NoteCardBorder.BorderBrush = borderBrush;
            TitleTextBox.Foreground = textBrush;
            RichEditor.Foreground = textBrush;

            Brush iconBrush = isDark
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8F4FF"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#222222"));
            Brush mutedBrush = isDark
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D8D0E4"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#666666"));
            Brush sidebarTextBrush = isDark
                ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F8F4FF"))
                : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#333333"));

            TitleTextBox.Foreground = iconBrush;
            StatusLabel.Foreground = mutedBrush;
            PinBadgeLabel.Foreground = mutedBrush;
            AudioStatusText.Foreground = mutedBrush;

            if (HeaderActionPanel != null)
            {
                foreach (Button button in HeaderActionPanel.Children.OfType<Button>())
                {
                    button.Foreground = sidebarTextBrush;
                }
            }

            if (DoneSidebarIcon != null)
            {
                DoneSidebarIcon.Foreground = isDark
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#9FF2B7"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
            }

            if (PinIconPath != null)
            {
                PinIconPath.Fill = sidebarTextBrush;
            }

            if (SidebarMenuBorder != null)
            {
                SidebarMenuBorder.Background = isDark 
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EE211A30"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#EEFFFFFF"));
                SidebarMenuBorder.BorderBrush = isDark
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#806F5B88"))
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#25000000"));
            }

            SaveNoteState();
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

        private void CollapseNote(bool collapse)
        {
            Note.IsCollapsed = collapse;
            if (collapse)
            {
                // Save the current expanded size before shrinking
                if (!Note.IsCollapsed || (Note.ExpandedWidth == 0 && Note.ExpandedHeight == 0))
                {
                    Note.ExpandedWidth  = Width  > 0 ? Width  : Note.Width;
                    Note.ExpandedHeight = Height > 0 ? Height : Note.Height;
                }

                FormatBarRow.Height = new GridLength(0);
                FormatToolbar.Visibility = Visibility.Collapsed;
                ContentGrid.Visibility = Visibility.Visible;
                AudioBarRow.Height = new GridLength(0);
                FooterBar.Visibility = Visibility.Collapsed;
                MinHeight = 90;
                Height = 90;
                FoldSvgIcon.Source = "/Resources/Icons/down-chevron-svgrepo-com.svg";

                // Disable resize when folded
                if (NoteResizeThumb != null) NoteResizeThumb.Visibility = Visibility.Collapsed;

                // Hide all sidebar buttons except Fold switch
                if (DoneSidebarButton != null) DoneSidebarButton.Visibility = Visibility.Collapsed;
                if (PinButton != null) PinButton.Visibility = Visibility.Collapsed;
                if (HideNoteButton != null) HideNoteButton.Visibility = Visibility.Collapsed;
                if (ThemeButton != null) ThemeButton.Visibility = Visibility.Collapsed;
                if (NewNoteButton != null) NewNoteButton.Visibility = Visibility.Collapsed;
                if (DeleteButton != null) DeleteButton.Visibility = Visibility.Collapsed;

                // Apply 3-Line Opacity Mask Fade
                LinearGradientBrush mask = new LinearGradientBrush();
                mask.StartPoint = new Point(0, 0);
                mask.EndPoint = new Point(0, 1);
                mask.GradientStops.Add(new GradientStop(Colors.Black, 0.0));
                mask.GradientStops.Add(new GradientStop(Colors.Black, 0.5));
                mask.GradientStops.Add(new GradientStop(Colors.Transparent, 1.0));
                RichEditor.OpacityMask = mask;
            }
            else
            {
                // Restore the saved expanded dimensions (fallback to Note.Width/Height or 240)
                double restoreW = Note.ExpandedWidth  > 0 ? Note.ExpandedWidth  : (Note.Width  > 0 ? Note.Width  : 240);
                double restoreH = Note.ExpandedHeight > 0 ? Note.ExpandedHeight : (Note.Height > 0 ? Note.Height : 240);

                MinHeight = 90;
                ContentGrid.Visibility = Visibility.Visible;
                RichEditor.OpacityMask = null;
                if (!string.IsNullOrEmpty(Note.AudioMemoPath)) AudioBarRow.Height = GridLength.Auto;
                FooterBar.Visibility = Visibility.Visible;
                Width  = restoreW;
                Height = restoreH;
                FoldSvgIcon.Source = "/Resources/Icons/up-chevron-svgrepo-com.svg";

                // Enable resize when expanded
                if (NoteResizeThumb != null) NoteResizeThumb.Visibility = Visibility.Visible;

                // Restore all sidebar buttons
                if (DoneSidebarButton != null) DoneSidebarButton.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
                if (PinButton != null) PinButton.Visibility = Visibility.Visible;
                if (HideNoteButton != null) HideNoteButton.Visibility = Visibility.Visible;
                if (ThemeButton != null) ThemeButton.Visibility = Visibility.Visible;
                if (NewNoteButton != null) NewNoteButton.Visibility = Visibility.Visible;
                if (DeleteButton != null) DeleteButton.Visibility = Visibility.Visible;

                // A pinned toolbar returns with the expanded note, even when the
                // note was collapsed while it was being edited.
                ApplyToolbarLayout();
            }
            SaveNoteState();
        }

        // ================= EDIT / VIEW MODES & HOVER REVEAL =================

        private void EnterEditMode()
        {
            _isEditMode = true;
            RichEditor.IsReadOnly = false;
            RichEditor.Cursor = Cursors.IBeam;
            TitleTextBox.IsReadOnly = false;
            TitleTextBox.Focusable = true;
            TitleTextBox.Cursor = Cursors.IBeam;

            ApplyToolbarLayout();
            if (!Note.IsCollapsed) DoneSidebarButton.Visibility = Visibility.Visible;
            if (FoldButton != null) FoldButton.Visibility = Visibility.Collapsed;
            SidebarMenuBorder.Opacity = 1.0;
            FooterBar.Opacity = 1.0;
            StatusLabel.Text = "Editing Mode | Click ✔ Done when finished";
        }

        private void ExitEditMode()
        {
            _isEditMode = false;
            RichEditor.IsReadOnly = true;
            RichEditor.Cursor = Cursors.Arrow;
            TitleTextBox.IsReadOnly = true;
            TitleTextBox.Focusable = false;
            TitleTextBox.Cursor = Cursors.Arrow;

            ApplyToolbarLayout();
            DoneSidebarButton.Visibility = Visibility.Collapsed;
            if (FoldButton != null) FoldButton.Visibility = Visibility.Visible;
            SidebarMenuBorder.Opacity = 0.0;
            FooterBar.Opacity = 0.0;

            UpdateWordCount();
            SaveNoteState();
        }

        private void TitleTextBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Note.IsCollapsed) CollapseNote(false);
            EnterEditMode();
            TitleTextBox.SelectAll();
        }

        private void RichEditor_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Note.IsCollapsed) CollapseNote(false);
            EnterEditMode();
        }

        private void RichEditor_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // The palette is intentionally persistent while it is being used, but text
            // selection is a new editing task and should always reclaim the workspace.
            if (ThemePopup.IsOpen)
            {
                ThemePopup.IsOpen = false;
            }

            if (_isEditMode && TryToggleChecklistMarker(e.GetPosition(RichEditor)))
            {
                e.Handled = true;
            }
        }

        private bool TryToggleChecklistMarker(Point point)
        {
            TextPointer? position = RichEditor.GetPositionFromPoint(point, true);
            Paragraph? paragraph = position?.Paragraph;
            if (position == null || paragraph == null) return false;

            int prefixLength = GetChecklistPrefixLength(paragraph);
            if (prefixLength == 0) return false;

            TextPointer? start = GetFirstTextPosition(paragraph);
            if (start == null) return false;
            int offset = start.GetOffsetToPosition(position);
            if (offset < 0 || offset > prefixLength + 1) return false;

            TextPointer? end = MoveForwardByTextCharacters(
                start,
                paragraph.ContentEnd,
                1);
            if (end == null) return false;

            var markerRange = new TextRange(start, end);
            markerRange.Text = markerRange.Text.StartsWith("☑", StringComparison.Ordinal)
                ? "☐"
                : "☑";
            SaveNoteState();
            return true;
        }

        private void RichEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isEditMode || e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            Paragraph? paragraph = RichEditor.CaretPosition.Paragraph;
            if (paragraph == null || GetChecklistPrefixLength(paragraph) == 0) return;

            string paragraphText = new TextRange(
                paragraph.ContentStart,
                paragraph.ContentEnd).Text;
            string contentWithoutMarker = paragraphText.Substring(
                Math.Min(GetChecklistPrefixLength(paragraph), paragraphText.Length));

            if (string.IsNullOrWhiteSpace(contentWithoutMarker))
            {
                RemoveChecklistMarker(paragraph);
                e.Handled = true;
                SaveNoteState();
                return;
            }

            Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    Paragraph? nextParagraph = RichEditor.CaretPosition.Paragraph;
                    if (nextParagraph != null && GetChecklistPrefixLength(nextParagraph) == 0)
                    {
                        InsertChecklistMarker(nextParagraph, "☐ ");
                        SaveNoteState();
                    }
                }));
        }

        private void RichEditor_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (ThemePopup.IsOpen)
            {
                ThemePopup.IsOpen = false;
            }
        }

        private void DoneEditButton_Click(object sender, RoutedEventArgs e)
        {
            ExitEditMode();
        }

        private void WindowRootGrid_MouseEnter(object sender, MouseEventArgs e)
        {
            _sidebarHideTimer.Stop();
            SidebarMenuBorder.Opacity = 1.0;
            FooterBar.Opacity = 1.0;
        }

        private void WindowRootGrid_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isEditMode)
            {
                _sidebarHideTimer.Stop();
                _sidebarHideTimer.Start();
            }
        }

        private void SidebarMenuBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            _sidebarHideTimer.Stop();
            SidebarMenuBorder.Opacity = 1.0;
            FooterBar.Opacity = 1.0;
        }

        private void SidebarMenuBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isEditMode)
            {
                _sidebarHideTimer.Stop();
                _sidebarHideTimer.Start();
            }
        }

        private void NoteResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            _isResizing = true;
        }

        private void NoteResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (Note.IsCollapsed) return;

            double newWidth = Width + e.HorizontalChange;
            double newHeight = Height + e.VerticalChange;

            if (newWidth >= MinWidth) Width = newWidth;
            if (newHeight >= MinHeight) Height = newHeight;
        }

        private void NoteResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _isResizing = false;

            // Persist the final user-chosen dimensions immediately after release
            if (!Note.IsCollapsed)
            {
                Note.Width = Width;
                Note.Height = Height;
                SaveNoteState();
            }

            // Now it is safe to send unpinned note back to desktop z-order
            if (!Note.IsPinned)
            {
                SendToDesktopBottom();
            }
        }

        private void WindowRootGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ThemePopup.IsOpen &&
                e.OriginalSource is DependencyObject source &&
                !IsDescendantOf(source, ThemePopupCard))
            {
                ThemePopup.IsOpen = false;
            }

            if (!_isEditMode && e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private static bool IsDescendantOf(
            DependencyObject? source,
            DependencyObject ancestor)
        {
            DependencyObject? current = source;
            while (current != null)
            {
                if (ReferenceEquals(current, ancestor)) return true;

                current = current is Visual visual
                    ? VisualTreeHelper.GetParent(visual)
                    : LogicalTreeHelper.GetParent(current);
            }

            return false;
        }

        // ================= HEADER & TOOLBAR EVENT HANDLERS =================

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void TitleTextBox_LostFocus(object sender, RoutedEventArgs e) => SaveNoteState();
        private void TitleTextBox_TextChanged(object sender, TextChangedEventArgs e) => SaveNoteState();

        private void ApplyToolbarLayout()
        {
            if (ToolbarPinButton != null)
            {
                ToolbarPinIcon.Source = Note.IsToolbarPinned
                    ? "/Resources/Icons/pin-slash.svg.svg"
                    : "/Resources/Icons/pin.svg.svg";
                ToolbarPinButton.ToolTip = Note.IsToolbarPinned
                    ? "Unpin writing tools"
                    : "Pin writing tools";
            }

            bool showToolbar = !Note.IsCollapsed && (_isEditMode || Note.IsToolbarPinned);
            FormatBarRow.Height = showToolbar ? GridLength.Auto : new GridLength(0);
            FormatToolbar.Visibility = showToolbar ? Visibility.Visible : Visibility.Collapsed;
            ApplyResponsiveToolbarLayout();
        }

        private void ApplyResponsiveToolbarLayout(double availableWidth = double.NaN)
        {
            if (ToolbarNormalLayout == null || ToolbarCompactLayout == null) return;

            if (double.IsNaN(availableWidth) || availableWidth <= 0)
            {
                availableWidth = NoteCardBorder?.ActualWidth ?? ActualWidth;
            }
            if (availableWidth <= 0) return;

            if (!_toolbarDensityInitialized)
            {
                _useCompactToolbar = availableWidth < 520;
                _useCondensedCompactToolbar = availableWidth < 300;
                _useMinimalCompactToolbar = availableWidth < 210;
                _toolbarDensityInitialized = true;
            }
            else
            {
                // Separate enter/exit points stop the toolbar flickering at a breakpoint.
                _useCompactToolbar = _useCompactToolbar
                    ? availableWidth < 550
                    : availableWidth < 520;
                _useCondensedCompactToolbar = _useCondensedCompactToolbar
                    ? availableWidth < 320
                    : availableWidth < 300;
                _useMinimalCompactToolbar = _useMinimalCompactToolbar
                    ? availableWidth < 230
                    : availableWidth < 210;
            }

            if (!_useCompactToolbar)
            {
                _useCondensedCompactToolbar = false;
                _useMinimalCompactToolbar = false;
            }

            ToolbarNormalLayout.Visibility = _useCompactToolbar
                ? Visibility.Collapsed
                : Visibility.Visible;
            ToolbarCompactLayout.Visibility = _useCompactToolbar
                ? Visibility.Visible
                : Visibility.Collapsed;
            CompactCoreFormattingGroup.Visibility = _useCondensedCompactToolbar
                ? Visibility.Collapsed
                : Visibility.Visible;
            CompactPrimaryActionGroup.Visibility = _useMinimalCompactToolbar
                ? Visibility.Collapsed
                : Visibility.Visible;
            FormatToolbar.Padding = _useCompactToolbar
                ? new Thickness(4, 4, 4, 4)
                : new Thickness(7, 5, 7, 5);
        }

        private void ToolbarPinButton_Click(object sender, RoutedEventArgs e)
        {
            Note.IsToolbarPinned = !Note.IsToolbarPinned;
            ApplyToolbarLayout();
            SaveNoteState();
        }

        private void CompactMoreButton_Click(object sender, RoutedEventArgs e)
        {
            ToolbarMorePopup.IsOpen = !ToolbarMorePopup.IsOpen;
        }

        private void PinButton_Click(object sender, RoutedEventArgs e) => SetPinnedState(!Note.IsPinned);
        private void HideNoteButton_Click(object sender, RoutedEventArgs e) => StickyNoteManager.Instance.HideNote(Note.Id);
        private void ThemeButton_Click(object sender, RoutedEventArgs e) => ThemePopup.IsOpen = !ThemePopup.IsOpen;
        private void AiButton_Click(object sender, RoutedEventArgs e) => AiPopup.IsOpen = true;
        private void FoldButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isEditMode) return;
            CollapseNote(!Note.IsCollapsed);
        }
        private void NewNoteButton_Click(object sender, RoutedEventArgs e) => StickyNoteManager.Instance.CreateNewNote("Quick Note", "", Note.ColorTheme);
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new StickyNoteConfirmDialog(Note.Title, Note.ColorTheme, this);
            if (dialog.ShowDialog() == true)
            {
                StickyNoteManager.Instance.DeleteNote(Note.Id);
            }
        }

        private void ThemeOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string theme)
            {
                ApplyColorTheme(theme);
            }
        }

        private void CustomHexApply_Click(object sender, RoutedEventArgs e)
        {
            string hex = CustomHexTextBox.Text.Trim();
            if (!string.IsNullOrEmpty(hex))
            {
                if (!hex.StartsWith("#")) hex = "#" + hex;
                ApplyColorTheme(hex);
            }
        }

        // FONT FAMILY SELECTOR
        private void FontFamilyCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || _isSynchronizingFontSelectors || RichEditor == null) return;
            if (sender is ComboBox selector &&
                selector.SelectedItem is ComboBoxItem item &&
                item.Content is string fontName)
            {
                TextSelection sel = RichEditor.Selection;
                if (sel != null && !sel.IsEmpty)
                {
                    sel.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily(fontName));
                }
                else
                {
                    RichEditor.FontFamily = new FontFamily(fontName);
                }
                SynchronizeComboSelection(
                    selector,
                    ReferenceEquals(selector, FontFamilyCombo)
                        ? CompactFontFamilyCombo
                        : FontFamilyCombo,
                    fontName);
                SaveNoteState();
            }
        }

        // FONT SIZE SELECTOR
        private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isUpdatingUi || _isSynchronizingFontSelectors || RichEditor == null) return;
            if (sender is ComboBox selector &&
                selector.SelectedItem is ComboBoxItem item &&
                double.TryParse(item.Content?.ToString(), out double size))
            {
                TextSelection sel = RichEditor.Selection;
                if (sel != null && !sel.IsEmpty)
                {
                    sel.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                }
                else
                {
                    RichEditor.FontSize = size;
                }
                SynchronizeComboSelection(
                    selector,
                    ReferenceEquals(selector, FontSizeCombo)
                        ? CompactFontSizeCombo
                        : FontSizeCombo,
                    item.Content?.ToString() ?? string.Empty);
                SaveNoteState();
            }
        }

        private void SynchronizeComboSelection(
            ComboBox source,
            ComboBox target,
            string selectedText)
        {
            if (target == null || ReferenceEquals(source, target)) return;

            ComboBoxItem? matchingItem = target.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Content?.ToString(),
                    selectedText,
                    StringComparison.Ordinal));
            if (matchingItem == null || ReferenceEquals(target.SelectedItem, matchingItem)) return;

            _isSynchronizingFontSelectors = true;
            try
            {
                target.SelectedItem = matchingItem;
            }
            finally
            {
                _isSynchronizingFontSelectors = false;
            }
        }

        // FORMATTING BUTTONS
        private void BtnBold_Click(object sender, RoutedEventArgs e) => ToggleFormatting(EditingCommands.ToggleBold);
        private void BtnItalic_Click(object sender, RoutedEventArgs e) => ToggleFormatting(EditingCommands.ToggleItalic);
        private void BtnUnderline_Click(object sender, RoutedEventArgs e) => ToggleFormatting(EditingCommands.ToggleUnderline);
        private void BtnStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            TextSelection sel = RichEditor.Selection;
            if (sel != null && !sel.IsEmpty)
            {
                var cur = sel.GetPropertyValue(Inline.TextDecorationsProperty);
                if (cur == TextDecorations.Strikethrough)
                    sel.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
                else
                    sel.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            }
        }

        // TEXT COLOR & HIGHLIGHT PICKERS
        private void BtnTextColor_Click(object sender, RoutedEventArgs e) => TextColorPopup.IsOpen = true;
        private void BtnHighlight_Click(object sender, RoutedEventArgs e) => HighlightPopup.IsOpen = true;

        private void TextColorOption_Click(object sender, RoutedEventArgs e)
        {
            TextColorPopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is string colorHex)
            {
                TextSelection sel = RichEditor.Selection;
                if (sel != null && !sel.IsEmpty)
                {
                    Brush brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                    sel.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                    SaveNoteState();
                }
            }
        }

        private void HighlightOption_Click(object sender, RoutedEventArgs e)
        {
            HighlightPopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is string colorHex)
            {
                TextSelection sel = RichEditor.Selection;
                if (sel != null && !sel.IsEmpty)
                {
                    Brush brush = colorHex == "Transparent" 
                        ? Brushes.Transparent 
                        : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                    sel.ApplyPropertyValue(TextElement.BackgroundProperty, brush);
                    SaveNoteState();
                }
            }
        }

        private void BtnH1_Click(object sender, RoutedEventArgs e) => ApplyFontSize(20, FontWeights.Bold);
        private void BtnH2_Click(object sender, RoutedEventArgs e) => ApplyFontSize(16, FontWeights.Bold);
        private void BtnBullet_Click(object sender, RoutedEventArgs e)
        {
            ApplyNativeList(EditingCommands.ToggleBullets);
        }

        private void BtnListMenu_Click(object sender, RoutedEventArgs e)
        {
            ToolbarMorePopup.IsOpen = false;
            ListPopup.PlacementTarget = sender as UIElement;
            ListPopup.IsOpen = true;
        }

        private void ListBullets_Click(object sender, RoutedEventArgs e)
        {
            ListPopup.IsOpen = false;
            ApplyNativeList(EditingCommands.ToggleBullets);
        }

        private void ListNumbered_Click(object sender, RoutedEventArgs e)
        {
            ListPopup.IsOpen = false;
            ApplyNativeList(EditingCommands.ToggleNumbering);
        }

        private void ListChecklist_Click(object sender, RoutedEventArgs e)
        {
            ListPopup.IsOpen = false;
            ToolbarMorePopup.IsOpen = false;

            List<Paragraph> paragraphs = GetSelectedParagraphs();
            if (paragraphs.Count == 0) return;

            bool removeChecklist = paragraphs.All(
                paragraph => GetChecklistPrefixLength(paragraph) > 0);

            if (!removeChecklist && paragraphs.Any(IsInsideNativeList))
            {
                RemoveCurrentNativeList(paragraphs[0]);
                paragraphs = GetSelectedParagraphs();
            }

            foreach (Paragraph paragraph in paragraphs)
            {
                RemoveChecklistMarker(paragraph);
                if (!removeChecklist)
                {
                    InsertChecklistMarker(paragraph, "☐ ");
                }
            }

            SaveNoteState();
        }

        private void BtnAlignLeft_Click(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Left);
        private void BtnAlignCenter_Click(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Center);
        private void BtnAlignRight_Click(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Right);
        private void BtnAlignJustify_Click(object sender, RoutedEventArgs e) => ApplyAlignment(TextAlignment.Justify);

        // MS WORD-STYLE ¶ LTR AND ¶ RTL PARAGRAPH DIRECTION BUTTONS
        private void BtnLtrParagraph_Click(object sender, RoutedEventArgs e) => ApplyParagraphFlowDirection(FlowDirection.LeftToRight);
        private void BtnRtlParagraph_Click(object sender, RoutedEventArgs e) => ApplyParagraphFlowDirection(FlowDirection.RightToLeft);

        private void ApplyParagraphFlowDirection(FlowDirection dir)
        {
            TextSelection sel = RichEditor.Selection;
            if (sel != null)
            {
                sel.ApplyPropertyValue(Block.FlowDirectionProperty, dir);
            }
        }

        private void ToggleFormatting(RoutedUICommand cmd) => cmd.Execute(null, RichEditor);

        private void ApplyNativeList(RoutedUICommand command)
        {
            ToolbarMorePopup.IsOpen = false;
            List<Paragraph> paragraphs = GetSelectedParagraphs();
            foreach (Paragraph paragraph in paragraphs)
            {
                RemoveChecklistMarker(paragraph);
            }
            command.Execute(null, RichEditor);
            SaveNoteState();
        }

        private void RemoveCurrentNativeList(Paragraph paragraph)
        {
            System.Windows.Documents.List? parentList = FindParentList(paragraph);
            if (parentList == null) return;

            RoutedUICommand command = IsNumberedMarker(parentList.MarkerStyle)
                ? EditingCommands.ToggleNumbering
                : EditingCommands.ToggleBullets;
            command.Execute(null, RichEditor);
        }

        private static bool IsInsideNativeList(Paragraph paragraph) =>
            FindParentList(paragraph) != null;

        private static System.Windows.Documents.List? FindParentList(Paragraph paragraph)
        {
            DependencyObject? current = paragraph.Parent;
            while (current != null)
            {
                if (current is System.Windows.Documents.List documentList)
                {
                    return documentList;
                }
                current = current is FrameworkContentElement contentElement
                    ? contentElement.Parent
                    : null;
            }
            return null;
        }

        private static bool IsNumberedMarker(TextMarkerStyle markerStyle) => markerStyle is
            TextMarkerStyle.Decimal or
            TextMarkerStyle.LowerLatin or
            TextMarkerStyle.UpperLatin or
            TextMarkerStyle.LowerRoman or
            TextMarkerStyle.UpperRoman;

        private List<Paragraph> GetSelectedParagraphs()
        {
            TextSelection selection = RichEditor.Selection;
            Paragraph? currentParagraph = selection.Start.Paragraph;
            if (selection.IsEmpty)
            {
                return currentParagraph == null
                    ? new List<Paragraph>()
                    : new List<Paragraph> { currentParagraph };
            }

            TextPointer selectionStart = selection.Start;
            TextPointer selectionEnd = selection.End;
            return EnumerateParagraphs(RichEditor.Document.Blocks)
                .Where(paragraph =>
                    paragraph.ContentEnd.CompareTo(selectionStart) >= 0 &&
                    paragraph.ContentStart.CompareTo(selectionEnd) <= 0)
                .ToList();
        }

        private static IEnumerable<Paragraph> EnumerateParagraphs(BlockCollection blocks)
        {
            foreach (Block block in blocks)
            {
                if (block is Paragraph paragraph)
                {
                    yield return paragraph;
                }
                else if (block is Section section)
                {
                    foreach (Paragraph child in EnumerateParagraphs(section.Blocks))
                    {
                        yield return child;
                    }
                }
                else if (block is System.Windows.Documents.List documentList)
                {
                    foreach (ListItem listItem in documentList.ListItems)
                    {
                        foreach (Paragraph child in EnumerateParagraphs(listItem.Blocks))
                        {
                            yield return child;
                        }
                    }
                }
                else if (block is Table table)
                {
                    foreach (TableRowGroup rowGroup in table.RowGroups)
                    {
                        foreach (TableRow row in rowGroup.Rows)
                        {
                            foreach (TableCell cell in row.Cells)
                            {
                                foreach (Paragraph child in EnumerateParagraphs(cell.Blocks))
                                {
                                    yield return child;
                                }
                            }
                        }
                    }
                }
            }
        }

        private static int GetChecklistPrefixLength(Paragraph paragraph)
        {
            string text = new TextRange(
                paragraph.ContentStart,
                paragraph.ContentEnd).Text;
            if (string.IsNullOrEmpty(text) || (text[0] != '☐' && text[0] != '☑'))
            {
                return 0;
            }

            int length = 1;
            while (length < text.Length &&
                   char.IsWhiteSpace(text[length]) &&
                   text[length] != '\r' &&
                   text[length] != '\n')
            {
                length++;
            }
            return length;
        }

        private static void InsertChecklistMarker(Paragraph paragraph, string marker)
        {
            var markerRun = new Run(marker);
            Inline? firstInline = paragraph.Inlines.FirstInline;
            if (firstInline == null)
            {
                paragraph.Inlines.Add(markerRun);
            }
            else
            {
                paragraph.Inlines.InsertBefore(firstInline, markerRun);
            }
        }

        private static void RemoveChecklistMarker(Paragraph paragraph)
        {
            int prefixLength = GetChecklistPrefixLength(paragraph);
            if (prefixLength == 0) return;

            TextPointer? start = GetFirstTextPosition(paragraph);
            if (start == null) return;
            TextPointer? end = MoveForwardByTextCharacters(
                start,
                paragraph.ContentEnd,
                prefixLength);
            if (end != null && start.CompareTo(end) < 0)
            {
                new TextRange(start, end).Text = string.Empty;
            }
        }

        private static TextPointer? GetFirstTextPosition(Paragraph paragraph)
        {
            TextPointer? current = paragraph.ContentStart;
            while (current != null && current.CompareTo(paragraph.ContentEnd) < 0)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    return current;
                }
                current = current.GetNextContextPosition(LogicalDirection.Forward);
            }
            return null;
        }

        private static TextPointer? MoveForwardByTextCharacters(
            TextPointer start,
            TextPointer limit,
            int characterCount)
        {
            TextPointer? current = start;
            int remaining = characterCount;
            while (current != null &&
                   current.CompareTo(limit) < 0 &&
                   remaining > 0)
            {
                if (current.GetPointerContext(LogicalDirection.Forward) ==
                    TextPointerContext.Text)
                {
                    string text = current.GetTextInRun(LogicalDirection.Forward);
                    int take = Math.Min(remaining, text.Length);
                    current = current.GetPositionAtOffset(
                        take,
                        LogicalDirection.Forward);
                    remaining -= take;
                }
                else
                {
                    current = current.GetNextContextPosition(LogicalDirection.Forward);
                }
            }
            return remaining == 0 ? current : null;
        }

        private void ApplyFontSize(double size, FontWeight weight)
        {
            TextSelection sel = RichEditor.Selection;
            if (sel != null && !sel.IsEmpty)
            {
                sel.ApplyPropertyValue(TextElement.FontSizeProperty, size);
                sel.ApplyPropertyValue(TextElement.FontWeightProperty, weight);
            }
        }

        private void ApplyAlignment(TextAlignment align)
        {
            TextSelection sel = RichEditor.Selection;
            if (sel != null)
            {
                sel.ApplyPropertyValue(Block.TextAlignmentProperty, align);
            }
        }

        // TWO-COLUMN LAYOUT SUPPORT
        private void ApplyColumnLayout(int cols)
        {
            Note.ColumnCount = cols;
            if (cols == 2)
            {
                RichEditor.Document.ColumnWidth = 110;
                RichEditor.Document.ColumnGap = 12;
                ColToggleBtn.ToolTip = "Use one column";
            }
            else
            {
                RichEditor.Document.ColumnWidth = double.NaN;
                ColToggleBtn.ToolTip = "Use two columns";
            }
            SaveNoteState();
        }

        private void ColToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyColumnLayout(Note.ColumnCount == 1 ? 2 : 1);
        }

        // LIBRETRANSLATION & TARGET LANGUAGE REPLACEMENT

        // Language metadata: code -> (flag emoji, display name)
        private static readonly Dictionary<string, (string Flag, string Name)> _langMeta = new()
        {
            ["en"] = ("🇺🇸", "English"),
            ["fa"] = ("🇮🇷", "Persian (فارسی)"),
            ["de"] = ("🇩🇪", "German (Deutsch)"),
            ["fr"] = ("🇫🇷", "French (Français)"),
            ["es"] = ("🇪🇸", "Spanish (Español)"),
            ["ar"] = ("🇸🇦", "Arabic (العربية)"),
            ["zh"] = ("🇨🇳", "Chinese (中文)"),
            ["ja"] = ("🇯🇵", "Japanese (日本語)"),
            ["ko"] = ("🇰🇷", "Korean (한국어)"),
            ["ru"] = ("🇷🇺", "Russian (Русский)"),
            ["it"] = ("🇮🇹", "Italian (Italiano)"),
            ["pt"] = ("🇵🇹", "Portuguese"),
            ["tr"] = ("🇹🇷", "Turkish (Türkçe)"),
            ["nl"] = ("🇳🇱", "Dutch (Nederlands)"),
            ["pl"] = ("🇵🇱", "Polish (Polski)"),
            ["sv"] = ("🇸🇪", "Swedish (Svenska)"),
            ["uk"] = ("🇺🇦", "Ukrainian (Українська)"),
            ["id"] = ("🇮🇩", "Indonesian"),
            ["vi"] = ("🇻🇳", "Vietnamese (Tiếng Việt)"),
            ["hi"] = ("🇮🇳", "Hindi (हिन्दी)"),
        };

        private void TranslateButton_Click(object sender, RoutedEventArgs e) => TranslatePopup.IsOpen = true;

        private async void TranslatePopup_Opened(object sender, EventArgs e)
        {
            TranslateLangList.Children.Clear();
            TranslateLangEmptyHint.Visibility = Visibility.Collapsed;

            HashSet<string> installedCodes;
            try
            {
                installedCodes = await LocalLibreTranslateManager.GetInstalledLanguageCodesAsync();
            }
            catch
            {
                installedCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "en" };
            }

            if (installedCodes.Count == 0)
            {
                TranslateLangEmptyHint.Visibility = Visibility.Visible;
                return;
            }

            // Show installed languages in a consistent order (meta-defined first, then unknown codes)
            var ordered = _langMeta.Keys
                .Where(k => installedCodes.Contains(k))
                .Concat(installedCodes.Where(c => !_langMeta.ContainsKey(c)).OrderBy(c => c));

            foreach (string code in ordered)
            {
                _langMeta.TryGetValue(code, out var meta);
                string flag = meta.Flag ?? "🌐";
                string name = meta.Name ?? code.ToUpperInvariant();

                var btn = new Button
                {
                    Style = (Style)FindResource("TranslateLanguageButton"),
                    Width = 185,
                    Height = 32,
                    Margin = new Thickness(0, 2, 0, 2),
                    HorizontalContentAlignment = HorizontalAlignment.Left,
                    Padding = new Thickness(8, 2, 8, 2),
                    Tag = code,
                    ToolTip = name
                };
                var panel = new StackPanel { Orientation = Orientation.Horizontal };
                panel.Children.Add(new TextBlock { Text = flag, FontSize = 13, Margin = new Thickness(0, 0, 8, 0) });
                panel.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = TryFindResource("AppTextBrush") as Brush ?? Brushes.Black,
                    VerticalAlignment = VerticalAlignment.Center
                });
                btn.Content = panel;
                btn.Click += TranslateLanguageOption_Click;
                TranslateLangList.Children.Add(btn);
            }

            if (TranslateLangList.Children.Count == 0)
                TranslateLangEmptyHint.Visibility = Visibility.Visible;
        }

        private async void TranslateLanguageOption_Click(object sender, RoutedEventArgs e)
        {
            TranslatePopup.IsOpen = false;
            if (sender is Button btn && btn.Tag is string targetLang)
            {
                Note.TargetTranslateLanguage = targetLang;
                TextSelection sel = RichEditor.Selection;
                string textToTranslate = sel != null && !sel.IsEmpty
                    ? sel.Text.Trim()
                    : new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd).Text.Trim();

                if (string.IsNullOrWhiteSpace(textToTranslate)) return;

                StatusLabel.Text = "Translating with LibreTranslate...";
                try
                {
                    AppSettings settings = ConfigManager.Load();
                    TranslationResult result = await LibreTranslateService.Instance.TranslateAsync(
                        textToTranslate, targetLang, settings.LibreTranslateEndpoint, settings.LibreTranslateApiKey);

                    if (!string.IsNullOrWhiteSpace(result.TranslatedText))
                    {
                        if (sel != null && !sel.IsEmpty)
                        {
                            sel.Text = result.TranslatedText;
                        }
                        else
                        {
                            RichEditor.Document.Blocks.Clear();
                            RichEditor.Document.Blocks.Add(new Paragraph(new Run(result.TranslatedText)));
                        }
                        SaveNoteState();
                        StatusLabel.Text = "Translation replaced!";
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Translation failed: {ex.Message}\nPlease make sure LibreTranslate is running or configured in KeyMapper Settings.", "KeyMapper Translate", MessageBoxButton.OK, MessageBoxImage.Warning);
                    StatusLabel.Text = "Translation failed.";
                }
            }
        }

        // INTERACTIVE DRAG-RESIZABLE IMAGES
        private void BtnInsertImage_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
            {
                InsertImageToDocument(dialog.FileName);
            }
        }

        private void OnRichEditorPaste(object sender, DataObjectPastingEventArgs e)
        {
            if (e.FormatToApply == DataFormats.Bitmap || e.SourceDataObject.GetDataPresent(DataFormats.Bitmap))
            {
                var img = Clipboard.GetImage();
                if (img != null)
                {
                    string filePath = Path.Combine(StickyNoteManager.Instance.MediaFolderPath, $"img_{Guid.NewGuid():N}.png");
                    using (FileStream fs = new FileStream(filePath, FileMode.Create))
                    {
                        PngBitmapEncoder encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(img));
                        encoder.Save(fs);
                    }
                    InsertImageToDocument(filePath);
                    e.CancelCommand();
                    e.Handled = true;
                }
            }
        }

        private void InsertImageToDocument(string path)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.DecodePixelWidth = 200;
                bitmap.EndInit();

                Image image = new Image
                {
                    Source = bitmap,
                    Width = 180,
                    Height = double.NaN,
                    Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 4, 0, 4)
                };

                Grid container = new Grid { Margin = new Thickness(0, 4, 0, 4) };
                container.Children.Add(image);

                // Corner Thumb for Drag-Resizing
                System.Windows.Controls.Primitives.Thumb resizeThumb = new System.Windows.Controls.Primitives.Thumb
                {
                    Width = 10,
                    Height = 10,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Bottom,
                    Cursor = Cursors.SizeNWSE,
                    Background = Brushes.DimGray,
                    Margin = new Thickness(2)
                };

                resizeThumb.DragDelta += (s, e) =>
                {
                    double newWidth = Math.Max(40, image.Width + e.HorizontalChange);
                    image.Width = newWidth;
                    SaveNoteState();
                };

                container.Children.Add(resizeThumb);

                InlineUIContainer uiContainer = new InlineUIContainer(container);
                Paragraph p = new Paragraph(uiContainer);
                RichEditor.Document.Blocks.Add(p);

                if (!Note.Images.Contains(path)) Note.Images.Add(path);
                SaveNoteState();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to insert image: {ex.Message}");
            }
        }

        // TABLE TOOLS & MANAGEMENT
        private void BtnTableMenu_Click(object sender, RoutedEventArgs e) => TablePopup.IsOpen = true;

        private void TableInsertNew_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table table = new Table { CellSpacing = 2, Margin = new Thickness(0, 4, 0, 4) };
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });
            table.Columns.Add(new TableColumn { Width = new GridLength(80) });

            TableRowGroup group = new TableRowGroup();
            TableRow headerRow = new TableRow();
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Header 1")) { FontWeight = FontWeights.Bold }));
            headerRow.Cells.Add(new TableCell(new Paragraph(new Run("Header 2")) { FontWeight = FontWeights.Bold }));
            group.Rows.Add(headerRow);

            TableRow dataRow = new TableRow();
            dataRow.Cells.Add(new TableCell(new Paragraph(new Run("Cell 1"))));
            dataRow.Cells.Add(new TableCell(new Paragraph(new Run("Cell 2"))));
            group.Rows.Add(dataRow);

            table.RowGroups.Add(group);
            RichEditor.Document.Blocks.Add(table);
            SaveNoteState();
        }

        private Table? GetSelectedTable()
        {
            try
            {
                TextPointer caret = RichEditor.CaretPosition;
                Block block = caret.Paragraph;
                DependencyObject parent = block;
                while (parent != null && !(parent is Table))
                {
                    parent = LogicalTreeHelper.GetParent(parent) ?? VisualTreeHelper.GetParent(parent);
                }
                return parent as Table;
            }
            catch
            {
                return null;
            }
        }

        private TableCell? GetSelectedTableCell()
        {
            try
            {
                TextPointer caret = RichEditor.CaretPosition;
                DependencyObject parent = caret.Paragraph;
                while (parent != null && !(parent is TableCell))
                {
                    parent = LogicalTreeHelper.GetParent(parent) ?? VisualTreeHelper.GetParent(parent);
                }
                return parent as TableCell;
            }
            catch
            {
                return null;
            }
        }

        private void TableCellBgOption_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            TableCell? cell = GetSelectedTableCell();
            if (cell != null && sender is Button btn && btn.Tag is string colorHex)
            {
                cell.Background = colorHex == "Transparent"
                    ? Brushes.Transparent
                    : new SolidColorBrush((Color)ColorConverter.ConvertFromString(colorHex));
                SaveNoteState();
            }
        }

        private void TableBorderOption_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table? table = GetSelectedTable();
            if (table != null && sender is Button btn && btn.Tag is string borderInfo)
            {
                string[] parts = borderInfo.Split(';');
                double thick = double.Parse(parts[0]);
                string hex = parts[1];

                table.BorderThickness = new Thickness(thick);
                table.BorderBrush = hex == "Transparent" ? Brushes.Transparent : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));

                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        foreach (var cell in row.Cells)
                        {
                            cell.BorderThickness = new Thickness(thick);
                            cell.BorderBrush = hex == "Transparent" ? Brushes.Transparent : new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                        }
                    }
                }
                SaveNoteState();
            }
        }

        private void TableRowAbove_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table? table = GetSelectedTable();
            if (table != null && table.RowGroups.Count > 0)
            {
                int colCount = table.Columns.Count > 0 ? table.Columns.Count : 2;
                TableRow newRow = new TableRow();
                for (int i = 0; i < colCount; i++)
                {
                    newRow.Cells.Add(new TableCell(new Paragraph(new Run("Cell"))));
                }
                table.RowGroups[0].Rows.Insert(0, newRow);
                SaveNoteState();
            }
        }

        private void TableRowBelow_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table? table = GetSelectedTable();
            if (table != null && table.RowGroups.Count > 0)
            {
                int colCount = table.Columns.Count > 0 ? table.Columns.Count : 2;
                TableRow newRow = new TableRow();
                for (int i = 0; i < colCount; i++)
                {
                    newRow.Cells.Add(new TableCell(new Paragraph(new Run("Cell"))));
                }
                table.RowGroups[0].Rows.Add(newRow);
                SaveNoteState();
            }
        }

        private void TableRowDelete_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table? table = GetSelectedTable();
            if (table != null && table.RowGroups.Count > 0 && table.RowGroups[0].Rows.Count > 0)
            {
                table.RowGroups[0].Rows.RemoveAt(table.RowGroups[0].Rows.Count - 1);
                SaveNoteState();
            }
        }

        private void TableColLeft_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table? table = GetSelectedTable();
            if (table != null)
            {
                table.Columns.Add(new TableColumn { Width = new GridLength(80) });
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        row.Cells.Add(new TableCell(new Paragraph(new Run("Col"))));
                    }
                }
                SaveNoteState();
            }
        }

        private void TableColRight_Click(object sender, RoutedEventArgs e) => TableColLeft_Click(sender, e);

        private void TableColDelete_Click(object sender, RoutedEventArgs e)
        {
            TablePopup.IsOpen = false;
            Table? table = GetSelectedTable();
            if (table != null && table.Columns.Count > 1)
            {
                table.Columns.RemoveAt(table.Columns.Count - 1);
                foreach (var group in table.RowGroups)
                {
                    foreach (var row in group.Rows)
                    {
                        if (row.Cells.Count > 0)
                            row.Cells.RemoveAt(row.Cells.Count - 1);
                    }
                }
                SaveNoteState();
            }
        }

        // VOICE MEMO RECORDING & PLAYBACK
        private void BtnVoiceMemo_Click(object sender, RoutedEventArgs e)
        {
            AudioBarRow.Height = GridLength.Auto;
            AudioBar.Visibility = Visibility.Visible;
            if (_audioRecorder.IsRecording) StopAudioRecording();
            else StartAudioRecording();
        }

        private void AudioRecordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_audioRecorder.IsRecording) StopAudioRecording();
            else StartAudioRecording();
        }

        private void StartAudioRecording()
        {
            string path = _audioRecorder.StartRecording(StickyNoteManager.Instance.MediaFolderPath);
            if (!string.IsNullOrEmpty(path))
            {
                Note.AudioMemoPath = path;
                AudioRecordButton.Content = "⏹";
                AudioStatusText.Text = "Recording... 0.0s";
            }
        }

        private void StopAudioRecording()
        {
            double duration = _audioRecorder.StopRecording();
            Note.AudioDurationSeconds = duration;
            AudioRecordButton.Content = "🔴";
            AudioStatusText.Text = $"Voice Memo ({duration:0.#}s)";
            SaveNoteState();
        }

        private void AudioPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_audioRecorder.IsPlaying)
            {
                _audioRecorder.StopPlayback();
            }
            else if (!string.IsNullOrEmpty(Note.AudioMemoPath) && File.Exists(Note.AudioMemoPath))
            {
                _audioRecorder.PlayAudio(Note.AudioMemoPath);
                AudioPlayButton.Content = "⏸";
            }
        }

        private void AudioDeleteButton_Click(object sender, RoutedEventArgs e)
        {
            _audioRecorder.StopPlayback();
            if (!string.IsNullOrEmpty(Note.AudioMemoPath) && File.Exists(Note.AudioMemoPath))
            {
                try { File.Delete(Note.AudioMemoPath); } catch { }
            }
            Note.AudioMemoPath = null;
            Note.AudioDurationSeconds = 0;
            AudioBar.Visibility = Visibility.Collapsed;
            AudioBarRow.Height = new GridLength(0);
            SaveNoteState();
        }

        private void OnPlaybackProgress(double currentSecs)
        {
            Dispatcher.Invoke(() =>
            {
                AudioStatusText.Text = $"Playing: {currentSecs:0.#}s / {Note.AudioDurationSeconds:0.#}s";
            });
        }

        private void OnPlaybackStopped()
        {
            Dispatcher.Invoke(() =>
            {
                AudioPlayButton.Content = "▶";
                AudioStatusText.Text = $"Voice Memo ({Note.AudioDurationSeconds:0.#}s)";
            });
        }

        private void OnRecordingTimeUpdated(double secs)
        {
            Dispatcher.Invoke(() =>
            {
                AudioStatusText.Text = $"Recording... {secs:0.#}s";
            });
        }

        // AI ASSISTANT ACTIONS (SUMMARIZE & FIX PRESERVING MEDIA & NO FILLER PREAMBLE)
        private async Task<string> CallAiAsync(string systemPrompt, string userText)
        {
            try
            {
                AppSettings settings = ConfigManager.Load();
                string modelId = !string.IsNullOrEmpty(settings.LocalAiModelId) ? settings.LocalAiModelId : "qwen3-0.6b-q8";
                
                string fullPrompt = $"{systemPrompt}\nCRITICAL INSTRUCTION: Return ONLY the direct revised text output. Do NOT write conversational intros, titles, or quotes like 'Here is the polished version:'.\n\nText:\n{userText}";
                string? result = await LocalAiService.Instance.GenerateAsync(modelId, "You are a precise text editor tool.", fullPrompt, 250);
                
                if (!string.IsNullOrWhiteSpace(result))
                {
                    string clean = Regex.Replace(result, @"^(Here is|Here's|Polished version|Revised text|Output)[^:\n]*:\s*", "", RegexOptions.IgnoreCase).Trim();
                    return clean;
                }
                return "";
            }
            catch
            {
                return "";
            }
        }

        private async void AiSummarize_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            string text = new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd).Text;
            if (string.IsNullOrWhiteSpace(text)) return;

            StatusLabel.Text = "Summarizing with Local AI...";
            string summary = await CallAiAsync("Summarize the input text into 2 short concise bullet points.", text);
            if (!string.IsNullOrWhiteSpace(summary))
            {
                RichEditor.Document.Blocks.Add(new Paragraph(new Run($"\n📌 Summary:\n{summary}")) { FontWeight = FontWeights.Bold });
                SaveNoteState();
                StatusLabel.Text = "Summary added!";
            }
            else
            {
                StatusLabel.Text = "Local AI model not loaded.";
            }
        }

        private async void AiFixGrammar_Click(object sender, RoutedEventArgs e)
        {
            AiPopup.IsOpen = false;
            TextRange range = new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd);
            string text = range.Text.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            StatusLabel.Text = "Fixing grammar with Local AI...";
            string fixedText = await CallAiAsync("Fix spelling and grammar errors in this text.", text);
            if (!string.IsNullOrWhiteSpace(fixedText))
            {
                range.Text = fixedText.Trim();
                SaveNoteState();
                StatusLabel.Text = "Grammar fixed!";
            }
            else
            {
                StatusLabel.Text = "Local AI model not loaded.";
            }
        }

        private void RichEditor_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (ThemePopup.IsOpen && RichEditor.Selection != null && !RichEditor.Selection.IsEmpty)
            {
                ThemePopup.IsOpen = false;
            }

            UpdateWordCount();
        }

        private void RichEditor_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateWordCount();
            SaveNoteState();
        }

        private void UpdateWordCount()
        {
            if (_isUpdatingUi || RichEditor?.Document == null) return;
            string text = new TextRange(RichEditor.Document.ContentStart, RichEditor.Document.ContentEnd).Text.Trim();
            int chars = text.Length;
            int words = string.IsNullOrWhiteSpace(text) ? 0 : Regex.Split(text, @"\s+").Length;
            if (!_isEditMode)
            {
                StatusLabel.Text = $"Double-click to edit | {words} words";
            }
            else
            {
                StatusLabel.Text = $"{words} words | {chars} chars";
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // Text changes are saved immediately, and this final write covers a
            // last title or document change made just before the window closes.
            SaveNoteState();
            _audioRecorder.Dispose();
            base.OnClosed(e);
        }
    }
}
