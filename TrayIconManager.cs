using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;

namespace KeyMapper
{
    public class TrayIconManager : IDisposable
    {
        private readonly NotifyIcon _notifyIcon;
        private readonly MainWindow _mainWindow;
        private readonly KeyboardHook _hook;

        private Icon? _currentIcon;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public TrayIconManager(MainWindow mainWindow, KeyboardHook hook)
        {
            _mainWindow = mainWindow;
            _hook = hook;

            _notifyIcon = new NotifyIcon
            {
                Text = "Desktop Pet - starting…",
                // An icon must be assigned before making NotifyIcon visible.  Creating
                // it visible first can leave it missing from the Windows notification area.
                Visible = false
            };

            // Set initial icon
            UpdateIconState(_hook.IsEnabled);

            // Context Menu
            var contextMenu = new ContextMenuStrip();
            
            var settingsItem = new ToolStripMenuItem("Settings", null, (s, e) => ShowMainWindow());
            settingsItem.Font = new Font(settingsItem.Font, System.Drawing.FontStyle.Bold);
            contextMenu.Items.Add(settingsItem);

            contextMenu.Items.Add(new ToolStripMenuItem("Show Desktop Pet", null, (s, e) => _mainWindow.ShowPetOverlayWindow()));
            contextMenu.Items.Add(new ToolStripMenuItem("Hide Desktop Pet", null, (s, e) => _mainWindow.HidePetOverlayWindow()));

            var enableItem = new ToolStripMenuItem("Enabled", null, (s, e) =>
            {
                var item = (ToolStripMenuItem)s!;
                item.Checked = !item.Checked;
                _hook.IsEnabled = item.Checked;
                UpdateIconState(_hook.IsEnabled);
                _mainWindow.UpdateHookStatusUI();
            });
            enableItem.Checked = _hook.IsEnabled;
            contextMenu.Items.Add(enableItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var openLogsItem = new ToolStripMenuItem("Open Action Log", null, (s, e) =>
            {
                try
                {
                    string folderPath = System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "KeyMapper"
                    );
                    string logPath = System.IO.Path.Combine(folderPath, "actions_log.txt");
                    if (System.IO.File.Exists(logPath))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = logPath,
                            UseShellExecute = true
                        });
                    }
                    else
                    {
                        System.Windows.MessageBox.Show("No actions have been logged yet.", "KeyMapper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Could not open logs: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                }
            });
            contextMenu.Items.Add(openLogsItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            contextMenu.Items.Add(new ToolStripMenuItem("Exit", null, (s, e) =>
            {
                System.Windows.Application.Current.Shutdown();
            }));

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.Visible = true;

            // Double click opens settings
            _notifyIcon.DoubleClick += (s, e) => ShowMainWindow();
        }

        public void UpdateIconState(bool isEnabled)
        {
            try
            {
                // Clean up previous icon to prevent GDI leak
                if (_currentIcon != null)
                {
                    _notifyIcon.Icon = null;
                    _currentIcon.Dispose();
                }

                _currentIcon = CreateDynamicIcon(isEnabled);
                _notifyIcon.Icon = _currentIcon;
                
                if (!isEnabled)
                {
                    _notifyIcon.Text = "KeyMapper - Disabled";
                }
                else if (_hook.IsPaused)
                {
                    _notifyIcon.Text = "KeyMapper - Paused (Scroll Lock to Resume)";
                }
                else
                {
                    _notifyIcon.Text = "KeyMapper - Active (Tap Left Ctrl to record)";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update tray icon: {ex.Message}");
            }
        }

        private Icon CreateDynamicIcon(bool isEnabled)
        {
            try
            {
                string iconPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "app_icon.ico");
                if (System.IO.File.Exists(iconPath))
                {
                    return new Icon(iconPath);
                }
            }
            catch { }

            using (Bitmap bmp = new Bitmap(16, 16))
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                
                // Active/Inactive/Paused colors
                Color backColor;
                if (!isEnabled)
                {
                    backColor = Color.FromArgb(142, 142, 147); // Gray
                }
                else if (_hook.IsPaused)
                {
                    backColor = Color.FromArgb(255, 149, 0); // Orange
                }
                else
                {
                    backColor = Color.FromArgb(0, 122, 255); // Blue
                }
                
                using (Brush brush = new SolidBrush(backColor))
                {
                    g.FillEllipse(brush, 0, 0, 15, 15);
                }

                // Draw a sleek white 'K'
                using (Font font = new Font("Segoe UI", 9, System.Drawing.FontStyle.Bold))
                using (Brush textBrush = new SolidBrush(Color.White))
                {
                    g.DrawString("K", font, textBrush, 2, 0);
                }

                IntPtr hIcon = bmp.GetHicon();
                return Icon.FromHandle(hIcon);
            }
        }

        public void ShowNotification(string title, string text, ToolTipIcon icon = ToolTipIcon.Info)
        {
            _notifyIcon.ShowBalloonTip(3000, title, text, icon);
        }

        private void ShowMainWindow()
        {
            _mainWindow.Show();
            _mainWindow.WindowState = WindowState.Normal;
            _mainWindow.Activate();
        }

        public void Dispose()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            
            if (_currentIcon != null)
            {
                _currentIcon.Dispose();
            }
        }
    }
}
