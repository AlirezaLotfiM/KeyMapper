using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class QuickAccessWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private readonly MainWindow _mainWindow;
        private readonly IntPtr _prevActiveWindow;
        private readonly string _selectionText;
        private readonly int _rawX;
        private readonly int _rawY;
        private bool _isClosing = false;

        public ObservableCollection<QuickAccessItem> Items { get; } = new ObservableCollection<QuickAccessItem>();
        private ICollectionView _collectionView;

        public QuickAccessWindow(MainWindow mainWindow, string selectionText, int rawX, int rawY)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _selectionText = selectionText;
            _rawX = rawX;
            _rawY = rawY;

            // Capture target window that had focus before the menu was triggered
            _prevActiveWindow = GetForegroundWindow();

            PopulateItems();

            _collectionView = CollectionViewSource.GetDefaultView(Items);
            _collectionView.Filter = FilterItem;
            MenuListBox.ItemsSource = _collectionView;

            if (MenuListBox.Items.Count > 0)
            {
                MenuListBox.SelectedIndex = 0;
            }

            Loaded += (s, e) => {
                SearchBox.Focus();
                
                // Adjust height dynamically based on items count to look compact
                double calculatedHeight = 50 + (Items.Count * 44);
                if (calculatedHeight > 450) calculatedHeight = 450;
                if (calculatedHeight < 150) calculatedHeight = 150;
                this.Height = calculatedHeight;

                // Enforce screen boundaries to keep window fully visible on screen
                try
                {
                    var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(_rawX, _rawY));
                    var workingArea = screen.WorkingArea;

                    double scaleX = 1.0;
                    double scaleY = 1.0;
                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        scaleX = source.CompositionTarget.TransformToDevice.M11;
                        scaleY = source.CompositionTarget.TransformToDevice.M22;
                    }

                    double workLeft = workingArea.Left / scaleX;
                    double workTop = workingArea.Top / scaleY;
                    double workWidth = workingArea.Width / scaleX;
                    double workHeight = workingArea.Height / scaleY;

                    double left = _rawX / scaleX;
                    double top = _rawY / scaleY;

                    // Enforce horizontal bounds
                    if (left + this.Width > workLeft + workWidth)
                    {
                        left = workLeft + workWidth - this.Width - 10;
                    }
                    if (left < workLeft) left = workLeft + 10;

                    // Enforce vertical bounds
                    if (top + this.Height > workTop + workHeight)
                    {
                        top = workTop + workHeight - this.Height - 10;
                    }
                    if (top < workTop) top = workTop + 10;

                    this.Left = left;
                    this.Top = top;
                }
                catch (Exception)
                {
                    double scaleX = 1.0;
                    double scaleY = 1.0;
                    PresentationSource source = PresentationSource.FromVisual(this);
                    if (source != null && source.CompositionTarget != null)
                    {
                        scaleX = source.CompositionTarget.TransformToDevice.M11;
                        scaleY = source.CompositionTarget.TransformToDevice.M22;
                    }
                    this.Left = _rawX / scaleX;
                    this.Top = _rawY / scaleY;
                }
            };
        }

        private void PopulateItems()
        {
            Items.Clear();

            // 1. Text selection functions (if text was grabbed)
            if (!string.IsNullOrEmpty(_selectionText))
            {
                SelectionHeader.Visibility = Visibility.Visible;
                SelectionTextPreview.Text = _selectionText.Length > 40 
                    ? _selectionText.Substring(0, 37) + "..." 
                    : _selectionText;

                Items.Add(new QuickAccessItem { Icon = "🔠", Title = "UPPERCASE", SubTitle = "Convert text to UPPERCASE", TypeDisplay = "FN", Key = "upper", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
                Items.Add(new QuickAccessItem { Icon = "🔡", Title = "lowercase", SubTitle = "Convert text to lowercase", TypeDisplay = "FN", Key = "lower", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
                Items.Add(new QuickAccessItem { Icon = "🔤", Title = "Title Case", SubTitle = "Convert text to Title Case", TypeDisplay = "FN", Key = "title", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
                Items.Add(new QuickAccessItem { Icon = "↔️", Title = "Reverse Text", SubTitle = "Reverse the order of characters", TypeDisplay = "FN", Key = "rev", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
                Items.Add(new QuickAccessItem { Icon = "🧮", Title = "Evaluate Math", SubTitle = "Calculate selection as formula", TypeDisplay = "FN", Key = "calc", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
                Items.Add(new QuickAccessItem { Icon = "📡", Title = "Morse Code", SubTitle = "Encode text to Morse code", TypeDisplay = "FN", Key = "morse", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
                Items.Add(new QuickAccessItem { Icon = "📻", Title = "Decode Morse", SubTitle = "Decode Morse code to text", TypeDisplay = "FN", Key = "unmorse", IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
            }

            // 2. Add Clipboard history items (last 5)
            var clipHistory = MainWindow.ClipboardHistory;
            int clipCount = Math.Min(5, clipHistory.Count);
            for (int i = 0; i < clipCount; i++)
            {
                string text = clipHistory[i];
                string preview = text.Replace("\r", " ").Replace("\n", " ").Trim();
                if (preview.Length > 30) preview = preview.Substring(0, 27) + "...";

                Items.Add(new QuickAccessItem 
                { 
                    Icon = "📋", 
                    Title = $"Paste Clip #{i + 1}", 
                    SubTitle = preview, 
                    TypeDisplay = "CLIP", 
                    Key = text,
                    IconBgColor = "#2630D158", // soft green
                    IconFgColor = "#FF30D158"  // bright green
                });
            }

            // 3. Add Text Expansions sorted by usage count descending
            var sortedReps = _mainWindow.Replacements.OrderByDescending(x => x.UsageCount);
            foreach (var rep in sortedReps)
            {
                string scope = string.IsNullOrEmpty(rep.AllowedProcess) ? "" : $" [{rep.AllowedProcess}]";
                Items.Add(new QuickAccessItem 
                { 
                    Icon = "📄", 
                    Title = rep.Shortcut, 
                    SubTitle = rep.Target + scope, 
                    TypeDisplay = "EXP", 
                    Key = rep.Shortcut,
                    Tag = rep,
                    IconBgColor = "#260A84FF", // soft blue
                    IconFgColor = "#FF0A84FF"  // bright blue
                });
            }

            // 4. Add App Actions sorted by usage count descending
            var sortedActs = _mainWindow.Actions.OrderByDescending(x => x.UsageCount);
            foreach (var act in sortedActs)
            {
                string scope = string.IsNullOrEmpty(act.AllowedProcess) ? "" : $" [{act.AllowedProcess}]";
                Items.Add(new QuickAccessItem 
                { 
                    Icon = "⚡", 
                    Title = act.Shortcut, 
                    SubTitle = act.Target + scope, 
                    TypeDisplay = "ACT", 
                    Key = act.Shortcut,
                    Tag = act,
                    IconBgColor = "#26FF9F0A", // soft orange
                    IconFgColor = "#FFFF9F0A"  // bright orange
                });
            }
        }

        private bool FilterItem(object obj)
        {
            if (obj is QuickAccessItem item)
            {
                string filterText = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(filterText)) return true;

                return item.Title.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                       item.SubTitle.Contains(filterText, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _collectionView.Refresh();
            if (MenuListBox.Items.Count > 0)
            {
                MenuListBox.SelectedIndex = 0;
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down)
            {
                e.Handled = true;
                MoveSelection(1);
            }
            else if (e.Key == Key.Up)
            {
                e.Handled = true;
                MoveSelection(-1);
            }
            else if (e.Key == Key.Enter)
            {
                e.Handled = true;
                ExecuteSelected();
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                _isClosing = true;
                this.Close();
            }
        }

        private void MoveSelection(int direction)
        {
            int count = MenuListBox.Items.Count;
            if (count == 0) return;

            int newIndex = MenuListBox.SelectedIndex + direction;
            if (MenuListBox.SelectedIndex == -1)
            {
                newIndex = direction > 0 ? 0 : count - 1;
            }

            if (newIndex >= 0 && newIndex < count)
            {
                MenuListBox.SelectedIndex = newIndex;
                MenuListBox.ScrollIntoView(MenuListBox.SelectedItem);
            }
        }

        private async void ExecuteSelected()
        {
            var selected = MenuListBox.SelectedItem as QuickAccessItem;
            if (selected == null) return;

            // Set closing flag before Hide to prevent Deactivated event from triggering close again
            _isClosing = true;
            this.Hide();

            // Restore active focus to the target window
            if (_prevActiveWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_prevActiveWindow);
                await Task.Delay(100); // Allow window focus transition
            }

            // Disable keyboard hook temporarily during typing to avoid intercepting ourselves
            _mainWindow.DisableKeyboardHookTemporarily();

            try
            {
                if (selected.TypeDisplay == "FN")
                {
                    string result = _mainWindow.ExecuteTextFunctionDirect(selected.Key, _selectionText);
                    if (!string.IsNullOrEmpty(result))
                    {
                        InputSimulator.SimulateReplacement(0, result, 0);
                        SoundManager.PlaySuccess();
                    }
                }
                else if (selected.TypeDisplay == "EXP")
                {
                    if (selected.Tag is ShortcutMapping mapping)
                    {
                        await _mainWindow.ProcessAndPerformReplacementDirect(mapping, mapping.Shortcut, 0);
                        SoundManager.PlaySuccess();
                    }
                }
                else if (selected.TypeDisplay == "ACT")
                {
                    _mainWindow.ExecuteActionDirectly(selected.Key);
                }
                else if (selected.TypeDisplay == "CLIP")
                {
                    InputSimulator.SimulateReplacement(0, selected.Key, 0);
                    SoundManager.PlaySuccess();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"QuickAccessWindow execution failed: {ex.Message}");
            }
            finally
            {
                _mainWindow.EnableKeyboardHook();
            }

            this.Close();
        }

        private void MenuListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ExecuteSelected();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            // Close the palette overlay when user clicks away, if not already closing
            if (!_isClosing)
            {
                _isClosing = true;
                this.Close();
            }
        }
    }

    public class QuickAccessItem
    {
        public string Icon { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string SubTitle { get; set; } = string.Empty;
        public string TypeDisplay { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public object? Tag { get; set; }
        public string IconBgColor { get; set; } = "#260A84FF";
        public string IconFgColor { get; set; } = "#FF0A84FF";
    }
}
