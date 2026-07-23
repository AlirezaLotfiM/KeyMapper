using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace KeyMapper
{
    public partial class CommandPaletteWindow : Window
    {
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        private readonly MainWindow _mainWindow;
        private readonly IntPtr _prevActiveWindow;
        private bool _isClosing = false;

        public ObservableCollection<PaletteItem> Items { get; } = new ObservableCollection<PaletteItem>();
        private ICollectionView _collectionView;

        public CommandPaletteWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            // Capture the window that currently has focus before we show the palette
            _prevActiveWindow = GetForegroundWindow();

            PopulateItems();
            _collectionView = CollectionViewSource.GetDefaultView(Items);
            _collectionView.Filter = FilterItem;

            ResultsList.ItemsSource = _collectionView;

            if (ResultsList.Items.Count > 0)
            {
                ResultsList.SelectedIndex = 0;
            }

            Loaded += (s, e) => {
                SearchBox.Focus();
                Keyboard.Focus(SearchBox);
            };
        }

        private void PopulateItems()
        {
            Items.Clear();

            // 1. Add Text expansions sorted by UsageCount descending
            var sortedReps = _mainWindow.Replacements.OrderByDescending(x => x.UsageCount);
            foreach (var rep in sortedReps)
            {
                string scope = string.IsNullOrEmpty(rep.AllowedProcess) ? "" : $" [{rep.AllowedProcess}]";
                Items.Add(new PaletteItem
                {
                    Icon = "📄",
                    Shortcut = rep.Shortcut,
                    Target = rep.Target,
                    DisplayShortcut = rep.Shortcut,
                    DisplayTarget = rep.Target + scope,
                    IsAction = false,
                    IconBgColor = "#260A84FF", // soft blue
                    IconFgColor = "#FF0A84FF"  // bright blue
                });
            }

            // 2. Add Actions sorted by UsageCount descending
            var sortedActs = _mainWindow.Actions.OrderByDescending(x => x.UsageCount);
            foreach (var act in sortedActs)
            {
                string scope = string.IsNullOrEmpty(act.AllowedProcess) ? "" : $" [{act.AllowedProcess}]";
                Items.Add(new PaletteItem
                {
                    Icon = "⚡",
                    Shortcut = act.Shortcut,
                    Target = act.Target,
                    DisplayShortcut = act.Shortcut,
                    DisplayTarget = act.Target + scope,
                    IsAction = true,
                    IconBgColor = "#26FF9F0A", // soft orange
                    IconFgColor = "#FFFF9F0A"  // bright orange
                });
            }

            // 3. Add Built-in Clipboard history macros
            for (int i = 1; i <= 5; i++)
            {
                Items.Add(new PaletteItem
                {
                    Icon = "📋",
                    Shortcut = $"c{i}",
                    Target = $"Clipboard history #{i}",
                    DisplayShortcut = $"c{i}",
                    DisplayTarget = $"Paste clipboard history #{i}",
                    IsAction = false,
                    IconBgColor = "#2630D158", // soft green
                    IconFgColor = "#FF30D158"  // bright green
                });
            }

            // 4. Add Case converters
            Items.Add(new PaletteItem { Icon = "🔠", Shortcut = "up", Target = "UPPERCASE case converter", DisplayShortcut = "up", DisplayTarget = "Convert clipboard to UPPERCASE", IsAction = false, IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
            Items.Add(new PaletteItem { Icon = "🔡", Shortcut = "low", Target = "lowercase case converter", DisplayShortcut = "low", DisplayTarget = "Convert clipboard to lowercase", IsAction = false, IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });
            Items.Add(new PaletteItem { Icon = "🔤", Shortcut = "cap", Target = "Title Case case converter", DisplayShortcut = "cap", DisplayTarget = "Convert clipboard to Title Case", IsAction = false, IconBgColor = "#26BF5AF2", IconFgColor = "#FFBF5AF2" });

            // 5. Add Built-in System Utilities
            Items.Add(new PaletteItem { Icon = "🔇", Shortcut = "mute", Target = "mute toggle", DisplayShortcut = "mute", DisplayTarget = "Toggle system volume mute", IsAction = true, IconBgColor = "#26FF453A", IconFgColor = "#FFFF453A" });
            Items.Add(new PaletteItem { Icon = "🔒", Shortcut = "lock", Target = "lock pc", DisplayShortcut = "lock", DisplayTarget = "Lock Windows workstation", IsAction = true, IconBgColor = "#26FF453A", IconFgColor = "#FFFF453A" });
            Items.Add(new PaletteItem { Icon = "🗑", Shortcut = "empty", Target = "empty bin", DisplayShortcut = "empty", DisplayTarget = "Empty Recycle Bin silently", IsAction = true, IconBgColor = "#26FF453A", IconFgColor = "#FFFF453A" });
            Items.Add(new PaletteItem { Icon = "🌐", Shortcut = "ip", Target = "local ip address", DisplayShortcut = "ip", DisplayTarget = "Copy Local IP to clipboard", IsAction = true, IconBgColor = "#26FF453A", IconFgColor = "#FFFF453A" });
        }

        private bool FilterItem(object obj)
        {
            if (obj is PaletteItem item)
            {
                string filterText = SearchBox.Text.Trim();
                if (string.IsNullOrEmpty(filterText)) return true;

                // Match shortcut keyword or target contents
                return item.Shortcut.Contains(filterText, StringComparison.OrdinalIgnoreCase) ||
                       item.Target.Contains(filterText, StringComparison.OrdinalIgnoreCase);
            }
            return false;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _collectionView.Refresh();
            if (ResultsList.Items.Count > 0)
            {
                ResultsList.SelectedIndex = 0;
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
            int count = ResultsList.Items.Count;
            if (count == 0) return;

            int newIndex = ResultsList.SelectedIndex + direction;
            if (ResultsList.SelectedIndex == -1)
            {
                newIndex = direction > 0 ? 0 : count - 1;
            }

            if (newIndex >= 0 && newIndex < count)
            {
                ResultsList.SelectedIndex = newIndex;
                ResultsList.ScrollIntoView(ResultsList.SelectedItem);
            }
        }

        private async void ExecuteSelected()
        {
            var selected = ResultsList.SelectedItem as PaletteItem;
            if (selected == null) return;

            // Set closing flag before Hide to prevent Deactivated event from triggering close again
            _isClosing = true;
            this.Hide();

            // Give the OS 80ms to restore focus to the previously active window
            if (_prevActiveWindow != IntPtr.Zero)
            {
                SetForegroundWindow(_prevActiveWindow);
                await Task.Delay(80);
            }

            if (selected.IsAction)
            {
                // Route action to MainWindow runner
                _mainWindow.ExecuteActionDirectly(selected.Shortcut);
            }
            else
            {
                // Route replacement to MainWindow runner
                _mainWindow.ExecuteReplacementDirectly(selected.Shortcut);
            }

            this.Close();
        }

        private void ResultsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

    public class PaletteItem
    {
        public string Icon { get; set; } = "📄";
        public string Shortcut { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public string DisplayShortcut { get; set; } = string.Empty;
        public string DisplayTarget { get; set; } = string.Empty;
        public bool IsAction { get; set; }
        public string IconBgColor { get; set; } = "#260A84FF";
        public string IconFgColor { get; set; } = "#FF0A84FF";
    }
}
