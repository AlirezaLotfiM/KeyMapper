using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace KeyMapper
{
    public partial class App : System.Windows.Application
    {
        private static Mutex? _mutex;
        private static bool _hasMutex = false;
        private static EventWaitHandle? _showInstanceEvent;
        private MainWindow? _mainWindow;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogError("AppDomain", args.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, args) =>
            {
                LogError("Dispatcher", args.Exception);
                args.Handled = true;
            };

            // The named mutex only needs to exist while the primary process is alive.
            // Do not acquire it: acquiring it here ties ownership to the startup thread
            // and caused ReleaseMutex to throw during WPF shutdown.
            _mutex = new Mutex(false, "KeyMapperSingleInstanceMutex", out bool createdNew);
            _hasMutex = createdNew;

            if (!createdNew)
            {
                // Signal existing primary instance to unhide MainWindow & Desktop Pet
                try
                {
                    using (var eventHandle = EventWaitHandle.OpenExisting("KeyMapperShowInstanceEvent"))
                    {
                        eventHandle.Set();
                    }
                }
                catch { }

                System.Windows.Application.Current.Shutdown();
                return;
            }

            _showInstanceEvent = new EventWaitHandle(false, EventResetMode.AutoReset, "KeyMapperShowInstanceEvent");
            ThreadPool.QueueUserWorkItem(_ => ListenForShowSignal());

            base.OnStartup(e);

            ThemeManager.Apply(ConfigManager.Load().ThemeName);
            _mainWindow = new MainWindow();
            this.MainWindow = _mainWindow;
            _ = LocalLibreTranslateManager.EnsureRunningAsync();

            // This is a pet-first application.  Keep the control centre available from
            // the tray, but do not make it the first thing the user sees at startup.
            _mainWindow.ShowPetOverlayWindow();
            _mainWindow.ShowStartupNotification();

            // The desktop shortcut intentionally passes --show. Respect it on the
            // first process too (the single-instance signal already handles later
            // shortcut launches).
            if (e.Args.Any(argument =>
                    string.Equals(argument, "--show", StringComparison.OrdinalIgnoreCase)))
            {
                _mainWindow.Show();
                _mainWindow.WindowState = WindowState.Normal;
                _mainWindow.Activate();
            }
        }

        private void ListenForShowSignal()
        {
            while (_showInstanceEvent != null)
            {
                try
                {
                    if (_showInstanceEvent.WaitOne())
                    {
                        Dispatcher.Invoke(() =>
                        {
                            if (_mainWindow != null)
                            {
                                _mainWindow.Show();
                                _mainWindow.WindowState = WindowState.Normal;
                                _mainWindow.Activate();
                                _mainWindow.ShowPetOverlayWindow();
                            }
                        });
                    }
                }
                catch { }
            }
        }

        private void LogError(string source, Exception? ex)
        {
            try
            {
                string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyMapper", "error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{source}] {ex?.ToString()}\n\n");
            }
            catch { }
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            if (_hasMutex)
            {
                _mainWindow?.Shutdown();

                _showInstanceEvent?.Dispose();
                _showInstanceEvent = null;

                _hasMutex = false;
            }

            _mutex?.Dispose();
            _mutex = null;

            base.OnExit(e);
        }
    }
}
