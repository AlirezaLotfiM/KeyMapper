using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace KeyMapper
{
    public class DesktopFenceItemViewModel
    {
        public DesktopFenceItem Item { get; }
        public string Title => Item.Title;
        public string TargetPath => Item.TargetPath;
        public bool IsDirectory => Item.IsDirectory;
        public ImageSource? ShellIcon { get; }

        public DesktopFenceItemViewModel(DesktopFenceItem item)
        {
            Item = item;
            ShellIcon = ShellIconHelper.GetIconForPath(item.TargetPath, item.IsDirectory);
        }
    }

    public partial class DesktopFenceWindow : Window
    {
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;

        private readonly DesktopFenceConfig _config;
        private bool _isEditingTitle;
        private double _expandedHeight = 340;
        private bool _isDarkMode = true;

        public string FenceId => _config.Id;
        public bool IsCollapsed => _config.IsCollapsed;

        public DesktopFenceWindow(DesktopFenceConfig config)
        {
            InitializeComponent();
            _config = config;

            Left = _config.Left;
            Top = _config.Top;
            Width = Math.Max(220, _config.Width);
            Height = Math.Max(120, _config.Height);
            _expandedHeight = Height;

            ApplyThemeAndOpacity();
            RenderConfig();

            LocationChanged += DesktopFenceWindow_LocationChanged;
            SizeChanged += DesktopFenceWindow_SizeChanged;
            Closed += DesktopFenceWindow_Closed;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            try
            {
                IntPtr hwnd = new WindowInteropHelper(this).Handle;
                // Hide from Alt+Tab switcher (WS_EX_TOOLWINDOW)
                int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TOOLWINDOW);
            }
            catch { }
        }

        private void UpdateClipping()
        {
            if (ActualWidth > 0 && ActualHeight > 0)
            {
                MainShell.Clip = new RectangleGeometry
                {
                    RadiusX = 16,
                    RadiusY = 16,
                    Rect = new Rect(0, 0, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4))
                };
            }
        }

        private void ApplyThemeAndOpacity()
        {
            ApplyThemeColors(_config.ColorTheme, _isDarkMode);
            ApplyBackgroundOpacity(_config.FenceOpacity);
        }

        private void ApplyBackgroundOpacity(double opacity)
        {
            _config.FenceOpacity = Math.Clamp(opacity, 0.15, 1.0);
            byte alpha = (byte)(_config.FenceOpacity * 255);

            if (MainShell.Background is SolidColorBrush shellBrush)
            {
                Color c = shellBrush.Color;
                MainShell.Background = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            }
            if (HeaderBorder.Background is SolidColorBrush headerBrush)
            {
                Color c = headerBrush.Color;
                byte headerAlpha = (byte)Math.Min(255, alpha + 30);
                HeaderBorder.Background = new SolidColorBrush(Color.FromArgb(headerAlpha, c.R, c.G, c.B));
            }
        }

        private void ApplyThemeColors(string themeName, bool darkMode)
        {
            _config.ColorTheme = string.IsNullOrWhiteSpace(themeName) ? "Lavender" : themeName;
            _isDarkMode = darkMode;

            (string headerHex, string textHex, string bodyHex, string borderHex) = (themeName, darkMode) switch
            {
                ("Warm Yellow", true) => ("#2C2A1E", "#FEF08A", "#1A1914", "#FDE047"),
                ("Warm Yellow", false) => ("#FEF08A", "#713F12", "#FEFCE8", "#CA8A04"),

                ("Pastel Pink", true) => ("#2C1E26", "#FBCFE8", "#1A1418", "#F472B6"),
                ("Pastel Pink", false) => ("#FBCFE8", "#831843", "#FDF2F8", "#DB2777"),

                ("Soft Mint", true) => ("#1E2C26", "#A7F3D0", "#141A17", "#34D399"),
                ("Soft Mint", false) => ("#A7F3D0", "#064E3B", "#ECFDF5", "#059669"),

                ("Sky Blue", true) => ("#1E262C", "#BAE6FD", "#14171A", "#38BDF8"),
                ("Sky Blue", false) => ("#BAE6FD", "#0C4A6E", "#F0F9FF", "#0284C7"),

                ("Dark Carbon", true) => ("#1E293B", "#F1F5F9", "#0F172A", "#64748B"),
                ("Dark Carbon", false) => ("#E2E8F0", "#0F172A", "#F8FAFC", "#475569"),

                ("Warm Cream", true) => ("#2C271E", "#FEF3C7", "#1A1714", "#F59E0B"),
                ("Warm Cream", false) => ("#FEF3C7", "#78350F", "#FFFBEB", "#D97706"),

                _ when darkMode => ("#221E2E", "#E9D5FF", "#181824", "#C084FC"), // Lavender Dark
                _ => ("#E9D5FF", "#581C87", "#FAF5FF", "#9333EA")               // Lavender Light
            };

            try
            {
                Color textCol = (Color)ColorConverter.ConvertFromString(textHex);
                TitleTxt.Foreground = new SolidColorBrush(textCol);

                Color bodyCol = (Color)ColorConverter.ConvertFromString(bodyHex);
                byte alpha = (byte)(_config.FenceOpacity * 255);
                MainShell.Background = new SolidColorBrush(Color.FromArgb(alpha, bodyCol.R, bodyCol.G, bodyCol.B));

                Color headCol = (Color)ColorConverter.ConvertFromString(headerHex);
                byte headAlpha = (byte)Math.Min(255, alpha + 30);
                HeaderBorder.Background = new SolidColorBrush(Color.FromArgb(headAlpha, headCol.R, headCol.G, headCol.B));

                Color borderCol = (Color)ColorConverter.ConvertFromString(borderHex);
                MainShell.BorderBrush = new SolidColorBrush(Color.FromArgb(0x44, borderCol.R, borderCol.G, borderCol.B));

                DarkLightIconTxt.Text = darkMode ? "🌙" : "☀️";
                DarkLightTextTxt.Text = darkMode ? "Dark Mode" : "Light Mode";
            }
            catch { }
        }

        private void RenderConfig()
        {
            TitleTxt.Text = _config.Title;
            TitleEditBox.Text = _config.Title;

            ApplyCollapsedState(_config.IsCollapsed);

            if (_config.Type == FenceType.FolderPortal && Directory.Exists(_config.FolderPortalPath))
            {
                RenderFolderPortal();
            }
            else
            {
                RenderCustomShortcuts();
            }
        }

        private void RenderCustomShortcuts()
        {
            FolderPortalPanel.Visibility = Visibility.Collapsed;
            ShortcutsItemsControl.Visibility = Visibility.Visible;

            var vms = _config.Items.Select(item => new DesktopFenceItemViewModel(item)).ToList();
            ShortcutsItemsControl.ItemsSource = vms;
            EmptyPromptTxt.Visibility = vms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void RenderFolderPortal()
        {
            ShortcutsItemsControl.Visibility = Visibility.Collapsed;
            FolderPortalPanel.Visibility = Visibility.Visible;
            FolderPortalPathTxt.Text = $"Portal: {_config.FolderPortalPath}";

            try
            {
                var portalVMs = new List<DesktopFenceItemViewModel>();
                var entries = Directory.GetFileSystemEntries(_config.FolderPortalPath).Take(60);
                foreach (var entry in entries)
                {
                    bool isDir = Directory.Exists(entry);
                    var item = new DesktopFenceItem
                    {
                        Title = Path.GetFileName(entry),
                        TargetPath = entry,
                        IsDirectory = isDir
                    };
                    portalVMs.Add(new DesktopFenceItemViewModel(item));
                }
                FolderPortalItemsControl.ItemsSource = portalVMs;
                EmptyPromptTxt.Visibility = portalVMs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch
            {
                EmptyPromptTxt.Visibility = Visibility.Visible;
                EmptyPromptTxt.Text = "Cannot access folder portal";
            }
        }

        private void ApplyCollapsedState(bool collapse)
        {
            _config.IsCollapsed = collapse;
            if (collapse)
            {
                if (Height > 48) _expandedHeight = Height;
                ContentGrid.Visibility = Visibility.Collapsed;
                Height = 48;
                CollapseTxt.Text = "▼";
            }
            else
            {
                ContentGrid.Visibility = Visibility.Visible;
                Height = Math.Max(140, _expandedHeight);
                CollapseTxt.Text = "▲";
            }
            UpdateClipping();
        }

        private void Window_MouseEnter(object sender, MouseEventArgs e)
        {
            AnimateSidebarOpacity(1.0);
        }

        private void Window_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!PalettePopup.IsOpen && !OpacityPopup.IsOpen)
            {
                AnimateSidebarOpacity(0.0);
            }
        }

        private void AnimateSidebarOpacity(double targetOpacity)
        {
            DoubleAnimation anim = new DoubleAnimation
            {
                To = targetOpacity,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
            };
            SidebarMenuBorder.BeginAnimation(OpacityProperty, anim);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                ApplyCollapsedState(!_config.IsCollapsed);
                DesktopFenceManager.Instance.SaveFences();
                return;
            }

            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void TitleTxt_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                StartEditingTitle();
                e.Handled = true;
            }
        }

        private void StartEditingTitle()
        {
            _isEditingTitle = true;
            TitleTxt.Visibility = Visibility.Collapsed;
            TitleEditBox.Visibility = Visibility.Visible;
            TitleEditBox.Focus();
            TitleEditBox.SelectAll();
        }

        private void FinishEditingTitle()
        {
            if (!_isEditingTitle) return;
            _isEditingTitle = false;

            string newTitle = TitleEditBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(newTitle))
            {
                _config.Title = newTitle;
                TitleTxt.Text = newTitle;
            }
            TitleEditBox.Visibility = Visibility.Collapsed;
            TitleTxt.Visibility = Visibility.Visible;

            DesktopFenceManager.Instance.SaveFences();
        }

        private void TitleEditBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                FinishEditingTitle();
            }
            else if (e.Key == Key.Escape)
            {
                _isEditingTitle = false;
                TitleEditBox.Visibility = Visibility.Collapsed;
                TitleTxt.Visibility = Visibility.Visible;
            }
        }

        private void TitleEditBox_LostFocus(object sender, RoutedEventArgs e)
        {
            FinishEditingTitle();
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    foreach (var file in files)
                    {
                        if (!_config.Items.Any(x => string.Equals(x.TargetPath, file, StringComparison.OrdinalIgnoreCase)))
                        {
                            _config.Items.Add(new DesktopFenceItem
                            {
                                Title = Path.GetFileNameWithoutExtension(file),
                                TargetPath = file,
                                IsDirectory = Directory.Exists(file)
                            });
                        }
                    }
                    _config.Type = FenceType.CustomShortcuts;
                    RenderConfig();
                    DesktopFenceManager.Instance.SaveFences();
                }
            }
        }

        private void ShortcutItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DesktopFenceItemViewModel vm)
            {
                LaunchItem(vm.TargetPath);
            }
        }

        private void PortalItem_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DesktopFenceItemViewModel vm)
            {
                LaunchItem(vm.TargetPath);
            }
        }

        private void ShortcutItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DesktopFenceItemViewModel vm)
            {
                ShowItemContextMenu(vm.Item, e);
            }
        }

        private void PortalItem_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.DataContext is DesktopFenceItemViewModel vm)
            {
                ShowItemContextMenu(vm.Item, e);
            }
        }

        private void ShowItemContextMenu(DesktopFenceItem item, MouseButtonEventArgs e)
        {
            var contextMenu = new ContextMenu();

            var launchMenuItem = new MenuItem { Header = "Launch / Open" };
            launchMenuItem.Click += (_, _) => LaunchItem(item.TargetPath);

            var openLocItem = new MenuItem { Header = "Open File Location" };
            openLocItem.Click += (_, _) => OpenFileLocation(item.TargetPath);

            var removeItem = new MenuItem { Header = "Remove Item" };
            removeItem.Click += (_, _) =>
            {
                _config.Items.Remove(item);
                RenderConfig();
                DesktopFenceManager.Instance.SaveFences();
            };

            contextMenu.Items.Add(launchMenuItem);
            contextMenu.Items.Add(openLocItem);
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(removeItem);

            contextMenu.IsOpen = true;
            e.Handled = true;
        }

        private static void LaunchItem(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string resolved = ShellIconHelper.ResolveFullPath(path);
                Process.Start(new ProcessStartInfo(resolved) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not launch item:\n{ex.Message}", "KeyMapper Fence", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void OpenFileLocation(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string resolved = ShellIconHelper.ResolveFullPath(path);
                if (File.Exists(resolved))
                {
                    Process.Start("explorer.exe", $"/select,\"{resolved}\"");
                }
                else if (Directory.Exists(resolved))
                {
                    Process.Start("explorer.exe", $"\"{resolved}\"");
                }
            }
            catch { }
        }

        private void PaletteMenuBtn_Click(object sender, RoutedEventArgs e)
        {
            PalettePopup.IsOpen = !PalettePopup.IsOpen;
        }

        private void OpacityMenuBtn_Click(object sender, RoutedEventArgs e)
        {
            OpacityPopup.IsOpen = !OpacityPopup.IsOpen;
        }

        private void ThemeCircle_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string themeName)
            {
                ApplyThemeColors(themeName, _isDarkMode);
                DesktopFenceManager.Instance.SaveFences();
                PalettePopup.IsOpen = false;
            }
        }

        private void DarkLightToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyThemeColors(_config.ColorTheme, !_isDarkMode);
            DesktopFenceManager.Instance.SaveFences();
            PalettePopup.IsOpen = false;
        }

        private void OpacityOption_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string valStr && double.TryParse(valStr, out double opVal))
            {
                ApplyBackgroundOpacity(opVal);
                DesktopFenceManager.Instance.SaveFences();
                OpacityPopup.IsOpen = false;
            }
        }

        private void EyeToggleBtn_Click(object sender, RoutedEventArgs e)
        {
            if (FolderPortalPathBar != null)
            {
                FolderPortalPathBar.Visibility = FolderPortalPathBar.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void AddItemBtn_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select File or Application to add to Fence",
                Filter = "All Files (*.*)|*.*|Applications (*.exe)|*.exe|Shortcuts (*.lnk)|*.lnk"
            };

            if (dialog.ShowDialog() == true)
            {
                string file = dialog.FileName;
                _config.Items.Add(new DesktopFenceItem
                {
                    Title = Path.GetFileNameWithoutExtension(file),
                    TargetPath = file,
                    IsDirectory = Directory.Exists(file)
                });
                _config.Type = FenceType.CustomShortcuts;
                RenderConfig();
                DesktopFenceManager.Instance.SaveFences();
            }
        }

        private void CollapseBtn_Click(object sender, RoutedEventArgs e)
        {
            ApplyCollapsedState(!_config.IsCollapsed);
            DesktopFenceManager.Instance.SaveFences();
        }

        private void DeleteFenceBtn_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show($"Are you sure you want to delete fence '{_config.Title}'?", "Delete Fence", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                DesktopFenceManager.Instance.RemoveFence(_config.Id);
            }
        }

        private void ResizeGrip_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_config.IsCollapsed)
            {
                Width = Math.Max(200, Width + e.HorizontalChange);
                Height = Math.Max(120, Height + e.VerticalChange);
                _expandedHeight = Height;
            }
        }

        private void DesktopFenceWindow_LocationChanged(object? sender, EventArgs e)
        {
            if (IsLoaded)
            {
                _config.Left = Left;
                _config.Top = Top;
                DesktopFenceManager.Instance.SaveFences();
            }
        }

        private void DesktopFenceWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateClipping();
            if (IsLoaded && !_config.IsCollapsed)
            {
                _config.Width = Width;
                _config.Height = Height;
                DesktopFenceManager.Instance.SaveFences();
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is Grid)
            {
                DragMove();
            }
        }

        private void DesktopFenceWindow_Closed(object? sender, EventArgs e)
        {
            DesktopFenceManager.Instance.RegisterWindowClosed(_config.Id);
        }
    }
}
