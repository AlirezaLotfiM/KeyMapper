using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace KeyMapper
{
    internal enum LocalTranslationState
    {
        ExternalServer,
        NotInstalled,
        Starting,
        Ready,
        Failed
    }

    internal sealed record LocalTranslationStatus(
        LocalTranslationState State,
        string Message);

    internal sealed record TranslationSetupProgress(
        string Message,
        double? Percent = null);

    internal static class LocalLibreTranslateManager
    {
        private const string LibreTranslateVersion = "1.9.6";
        private const string PythonVersion = "3.13.11";
        private static readonly HttpClient DownloadClient = new()
        {
            Timeout = TimeSpan.FromMinutes(20)
        };
        private static readonly SemaphoreSlim OperationGate = new(1, 1);

        private static readonly string TranslationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KeyMapper",
            "Translation");
        private static readonly string PythonRoot = Path.Combine(TranslationRoot, "Python");
        private static readonly string PythonExecutable = Path.Combine(PythonRoot, "python.exe");
        private static readonly string LocalServerExecutable = Path.Combine(
            PythonRoot,
            "Scripts",
            "libretranslate.exe");
        private static readonly string DataRoot = Path.Combine(TranslationRoot, "Data");
        private static readonly string ConfigRoot = Path.Combine(TranslationRoot, "Config");
        private static readonly string CacheRoot = Path.Combine(TranslationRoot, "Cache");

        private static Process? _serverProcess;

        public static string InstallLocation => TranslationRoot;
        public static bool HasPrivateInstallation =>
            File.Exists(PythonExecutable) && File.Exists(LocalServerExecutable);
        public static bool HasAnyInstallation => FindServerExecutable() != null;

        public static bool UsesLocalEndpoint(string endpointText) =>
            TryGetLocalEndpoint(endpointText, out _, out _);

        public static async Task<LocalTranslationStatus> EnsureRunningAsync(
            CancellationToken cancellationToken = default)
        {
            AppSettings settings = ConfigManager.Load();
            if (!TryGetLocalEndpoint(settings.LibreTranslateEndpoint, out string host, out int port))
            {
                return new LocalTranslationStatus(
                    LocalTranslationState.ExternalServer,
                    "Using the LibreTranslate server configured below.");
            }

            if (await CanConnectAsync(host, port, cancellationToken))
            {
                return new LocalTranslationStatus(
                    LocalTranslationState.Ready,
                    "Local translation is ready.");
            }

            string? executable = FindServerExecutable();
            if (executable == null)
            {
                return new LocalTranslationStatus(
                    LocalTranslationState.NotInstalled,
                    "Local translation is not installed on this computer.");
            }

            await OperationGate.WaitAsync(cancellationToken);
            try
            {
                if (await CanConnectAsync(host, port, cancellationToken))
                {
                    return new LocalTranslationStatus(
                        LocalTranslationState.Ready,
                        "Local translation is ready.");
                }

                StartServer(executable, host, port);
                bool ready = await WaitUntilReadyAsync(
                    host,
                    port,
                    TimeSpan.FromMinutes(2),
                    null,
                    cancellationToken);

                return ready
                    ? new LocalTranslationStatus(
                        LocalTranslationState.Ready,
                        "Local translation is ready.")
                    : new LocalTranslationStatus(
                        LocalTranslationState.Failed,
                        "The local translator was found but did not finish starting.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new LocalTranslationStatus(
                    LocalTranslationState.Failed,
                    $"Local translation could not start: {FriendlyMessage(ex)}");
            }
            finally
            {
                OperationGate.Release();
            }
        }

        public static async Task<LocalTranslationStatus> InstallOrRepairAsync(
            IProgress<TranslationSetupProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            await OperationGate.WaitAsync(cancellationToken);
            string runtimeArchivePath = Path.Combine(
                Path.GetTempPath(),
                $"KeyMapper-Python-{PythonVersion}-{Guid.NewGuid():N}.zip");
            string pipBootstrapPath = Path.Combine(
                Path.GetTempPath(),
                $"KeyMapper-get-pip-{Guid.NewGuid():N}.py");

            try
            {
                Directory.CreateDirectory(TranslationRoot);

                if (!File.Exists(PythonExecutable))
                {
                    progress?.Report(new TranslationSetupProgress(
                        "Downloading the private translation runtime...",
                        0));
                    await DownloadFileAsync(
                        GetPythonRuntimeUrl(),
                        runtimeArchivePath,
                        "Downloading the private translation runtime...",
                        progress,
                        cancellationToken);

                    progress?.Report(new TranslationSetupProgress(
                        "Preparing the private translation runtime...",
                        null));
                    Directory.CreateDirectory(PythonRoot);
                    ZipFile.ExtractToDirectory(
                        runtimeArchivePath,
                        PythonRoot,
                        overwriteFiles: true);
                    EnablePythonSitePackages();
                }

                string pipExecutable = Path.Combine(PythonRoot, "Scripts", "pip.exe");
                if (!File.Exists(pipExecutable))
                {
                    await DownloadFileAsync(
                        "https://bootstrap.pypa.io/get-pip.py",
                        pipBootstrapPath,
                        "Preparing Python's package manager...",
                        progress,
                        cancellationToken);
                    await RunProcessAsync(
                        PythonExecutable,
                        $"\"{pipBootstrapPath}\" --disable-pip-version-check --no-warn-script-location",
                        TranslationRoot,
                        cancellationToken);
                }

                progress?.Report(new TranslationSetupProgress(
                    "Installing LibreTranslate and its language engine...",
                    null));
                await RunProcessAsync(
                    PythonExecutable,
                    $"-m pip install --disable-pip-version-check --no-warn-script-location --upgrade libretranslate=={LibreTranslateVersion}",
                    TranslationRoot,
                    cancellationToken);

                if (!File.Exists(LocalServerExecutable))
                    throw new InvalidOperationException(
                        "LibreTranslate finished installing, but its launcher was not created.");

                progress?.Report(new TranslationSetupProgress(
                    "Preparing English, German, and Persian language models. The first setup can take several minutes...",
                    null));
                AppSettings settings = ConfigManager.Load();
                string serverHost = "127.0.0.1";
                int serverPort = 5000;
                TryGetLocalEndpoint(
                    settings.LibreTranslateEndpoint,
                    out serverHost,
                    out serverPort);
                StartServer(LocalServerExecutable, serverHost, serverPort);
                bool ready = await WaitUntilReadyAsync(
                    serverHost,
                    serverPort,
                    TimeSpan.FromMinutes(8),
                    progress,
                    cancellationToken);

                if (!ready)
                    throw new TimeoutException(
                        "The language models did not finish loading in time. Use Repair to try again.");

                progress?.Report(new TranslationSetupProgress(
                    "Local translation is ready.",
                    100));
                return new LocalTranslationStatus(
                    LocalTranslationState.Ready,
                    "Local translation is ready.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new LocalTranslationStatus(
                    LocalTranslationState.Failed,
                    $"Setup could not finish: {FriendlyMessage(ex)}");
            }
            finally
            {
                try
                {
                    if (File.Exists(runtimeArchivePath))
                        File.Delete(runtimeArchivePath);
                    if (File.Exists(pipBootstrapPath))
                        File.Delete(pipBootstrapPath);
                }
                catch { }

                OperationGate.Release();
            }
        }

        public static async Task<LocalTranslationStatus> RemoveAsync(
            CancellationToken cancellationToken = default)
        {
            await OperationGate.WaitAsync(cancellationToken);
            try
            {
                StopPrivateServer();
                await Task.Delay(350, cancellationToken);

                if (Directory.Exists(TranslationRoot))
                    Directory.Delete(TranslationRoot, true);

                return new LocalTranslationStatus(
                    LocalTranslationState.NotInstalled,
                    "Local translation was removed from this computer.");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new LocalTranslationStatus(
                    LocalTranslationState.Failed,
                    $"Local translation could not be removed: {FriendlyMessage(ex)}");
            }
            finally
            {
                OperationGate.Release();
            }
        }

        private static async Task DownloadFileAsync(
            string url,
            string destination,
            string progressLabel,
            IProgress<TranslationSetupProgress>? progress,
            CancellationToken cancellationToken)
        {
            using HttpResponseMessage response = await DownloadClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            long? total = response.Content.Headers.ContentLength;
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream target = new(
                destination,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                true);

            byte[] buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total is > 0)
                {
                    progress?.Report(new TranslationSetupProgress(
                        $"{progressLabel} {received / 1048576d:0.0} of {total.Value / 1048576d:0.0} MB",
                        received * 100d / total.Value));
                }
            }
        }

        private static void EnablePythonSitePackages()
        {
            string? pathFile = Directory.GetFiles(PythonRoot, "python*._pth")
                .FirstOrDefault();
            if (pathFile == null)
                throw new InvalidOperationException(
                    "The private Python runtime did not contain its path configuration.");

            string contents = File.ReadAllText(pathFile);
            if (contents.Contains("#import site", StringComparison.Ordinal))
            {
                contents = contents.Replace(
                    "#import site",
                    "import site",
                    StringComparison.Ordinal);
            }
            else if (!contents.Contains("import site", StringComparison.Ordinal))
            {
                contents += $"{Environment.NewLine}import site{Environment.NewLine}";
            }

            File.WriteAllText(pathFile, contents);
        }

        private static async Task RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            ApplyPrivateEnvironment(startInfo);

            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException($"Windows could not start {Path.GetFileName(fileName)}.");
            using CancellationTokenRegistration cancellationRegistration =
                cancellationToken.Register(() =>
                {
                    try
                    {
                        if (!process.HasExited)
                            process.Kill(true);
                    }
                    catch { }
                });
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(error) ? output : error;
                detail = detail.Trim();
                if (detail.Length > 360)
                    detail = detail[^360..];
                throw new InvalidOperationException(
                    $"{Path.GetFileName(fileName)} exited with code {process.ExitCode}. {detail}");
            }
        }

        private static void StartServer(string executable, string host, int port)
        {
            if (_serverProcess is { HasExited: false })
                return;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments =
                    $"--host {host} --port {port} --load-only en,de,fa --disable-web-ui",
                WorkingDirectory = TranslationRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            ApplyPrivateEnvironment(startInfo);
            _serverProcess = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Windows could not start LibreTranslate.");
        }

        private static void ApplyPrivateEnvironment(ProcessStartInfo startInfo)
        {
            Directory.CreateDirectory(DataRoot);
            Directory.CreateDirectory(ConfigRoot);
            Directory.CreateDirectory(CacheRoot);
            startInfo.Environment["PYTHONUTF8"] = "1";
            startInfo.Environment["ARGOS_DEVICE_TYPE"] = "cpu";
            startInfo.Environment["XDG_DATA_HOME"] = DataRoot;
            startInfo.Environment["XDG_CONFIG_HOME"] = ConfigRoot;
            startInfo.Environment["XDG_CACHE_HOME"] = CacheRoot;
        }

        private static async Task<bool> WaitUntilReadyAsync(
            string host,
            int port,
            TimeSpan timeout,
            IProgress<TranslationSetupProgress>? progress,
            CancellationToken cancellationToken)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            while (stopwatch.Elapsed < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await CanConnectAsync(host, port, cancellationToken))
                    return true;

                if (_serverProcess is { HasExited: true })
                    return false;

                progress?.Report(new TranslationSetupProgress(
                    $"Preparing language models... {stopwatch.Elapsed:mm\\:ss}",
                    null));
                await Task.Delay(1000, cancellationToken);
            }

            return false;
        }

        private static async Task<bool> CanConnectAsync(
            string host,
            int port,
            CancellationToken cancellationToken)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(host, port, cancellationToken)
                    .AsTask()
                    .WaitAsync(TimeSpan.FromMilliseconds(700), cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetLocalEndpoint(
            string endpointText,
            out string host,
            out int port)
        {
            host = "127.0.0.1";
            port = 5000;
            string value = string.IsNullOrWhiteSpace(endpointText)
                ? "http://localhost:5000"
                : endpointText.Trim();

            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? endpoint) ||
                !endpoint.IsLoopback)
                return false;

            host = endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                ? "127.0.0.1"
                : endpoint.Host;
            port = endpoint.IsDefaultPort ? 5000 : endpoint.Port;
            return true;
        }

        private static string? FindServerExecutable()
        {
            if (File.Exists(LocalServerExecutable))
                return LocalServerExecutable;

            // Keep existing developer installations working, but public releases
            // always install into the per-user location above.
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
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

        private static string GetPythonRuntimeUrl()
        {
            string fileName = RuntimeInformation.OSArchitecture switch
            {
                Architecture.Arm64 => $"python-{PythonVersion}-embed-arm64.zip",
                Architecture.X86 => $"python-{PythonVersion}-embed-win32.zip",
                _ => $"python-{PythonVersion}-embed-amd64.zip"
            };
            return $"https://www.python.org/ftp/python/{PythonVersion}/{fileName}";
        }

        private static void StopPrivateServer()
        {
            try
            {
                if (_serverProcess is { HasExited: false })
                {
                    _serverProcess.Kill(true);
                    _serverProcess.WaitForExit(3000);
                }
            }
            catch { }
            finally
            {
                _serverProcess?.Dispose();
                _serverProcess = null;
            }

            foreach (Process process in Process.GetProcessesByName("python"))
            {
                try
                {
                    string? executable = process.MainModule?.FileName;
                    if (executable != null &&
                        Path.GetFullPath(executable).Equals(
                            Path.GetFullPath(PythonExecutable),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill(true);
                        process.WaitForExit(3000);
                    }
                }
                catch { }
                finally
                {
                    process.Dispose();
                }
            }
        }

        private static string FriendlyMessage(Exception exception)
        {
            if (exception is HttpRequestException)
                return
                    "The required files could not be downloaded. Check the internet connection and try again. " +
                    $"Windows reported: {exception.Message}";
            return exception.Message;
        }
    }
}
