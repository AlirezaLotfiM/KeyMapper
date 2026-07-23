using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KeyMapper
{
    internal static class LocalLibreTranslateManager
    {
        private static int _startAttempted;

        public static async Task EnsureRunningAsync()
        {
            if (Interlocked.Exchange(ref _startAttempted, 1) != 0)
                return;

            try
            {
                AppSettings settings = ConfigManager.Load();
                if (!Uri.TryCreate(settings.LibreTranslateEndpoint, UriKind.Absolute, out Uri? endpoint) ||
                    !endpoint.IsLoopback)
                    return;

                int port = endpoint.IsDefaultPort ? 5000 : endpoint.Port;
                string connectionHost = endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                    ? "127.0.0.1"
                    : endpoint.Host;
                if (await CanConnectAsync(connectionHost, port))
                    return;

                string? executable = FindBundledServer();
                if (executable == null)
                    return;

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = "--load-only en,de,fa --disable-web-ui",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                startInfo.Environment["PYTHONUTF8"] = "1";
                Process.Start(startInfo);
            }
            catch
            {
                // The translator gives a clear, actionable offline message if the
                // optional local service cannot be started.
            }
        }

        private static async Task<bool> CanConnectAsync(string host, int port)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port)
                    .WaitAsync(TimeSpan.FromMilliseconds(500));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static string? FindBundledServer()
        {
            DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (int level = 0; level < 7 && directory != null; level++, directory = directory.Parent)
            {
                string candidate = Path.Combine(
                    directory.FullName,
                    ".libretranslate",
                    "Scripts",
                    "libretranslate.exe");
                if (File.Exists(candidate))
                    return candidate;
            }

            return null;
        }
    }
}
