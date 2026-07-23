using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace KeyMapper
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private MainWindow? _mainWindow;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            // Enforce single instance using a system-wide Mutex
            _mutex = new Mutex(true, "KeyMapperSingleInstanceMutex", out bool createdNew);
            if (!createdNew)
            {
                System.Windows.MessageBox.Show("KeyMapper is already running in the system tray.", "KeyMapper", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                System.Windows.Application.Current.Shutdown();
                return;
            }

            base.OnStartup(e);

            // Instantiate main configuration window
            _mainWindow = new MainWindow();
            this.MainWindow = _mainWindow;

            // Always start minimized to the system tray by default.
            // Use '--show' to force showing the settings window on launch.
            bool forceShow = e.Args.Contains("--show");
            if (forceShow)
            {
                _mainWindow.Show();
            }
            else
            {
                _mainWindow.ShowStartupNotification();
            }
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            // Call custom Shutdown to dispose hooks and notifyicon
            if (_mainWindow != null)
            {
                _mainWindow.Shutdown();
            }

            _mutex?.ReleaseMutex();
            _mutex?.Dispose();

            base.OnExit(e);
        }
    }
}
