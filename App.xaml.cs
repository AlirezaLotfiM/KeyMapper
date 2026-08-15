using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            ThemeManager.Apply(ConfigManager.Load().ThemeName);

            try
            {
                _mainWindow = new MainWindow();
                this.MainWindow = _mainWindow;
            }
            catch (Exception ex)
            {
                LogError("Startup-MainWindowException", ex);
            }

            // Start translator asynchronously in background without blocking UI thread startup
            Task.Run(async () =>
            {
                try
                {
                    await LocalLibreTranslateManager.EnsureRunningAsync();
                }
                catch { }
            });

            // Initialize Super Sticky Notes
            StickyNoteManager.Instance.Initialize();

            // Initialize Desktop Fences
            DesktopFenceManager.Instance.Initialize();

            // Start quietly in the system tray. The dashboard remains available
            // from the tray icon, notification click, or an explicit command.
            if (_mainWindow != null)
            {
                _mainWindow.ShowStartupNotification();
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
            LogError("App-OnExit", new Exception($"App exiting with code {e.ApplicationExitCode}"));
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
