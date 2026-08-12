using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace KeyMapper
{
    public class CoreManagerService
    {
        private Process? _coreProcess;
        private CancellationTokenSource? _statsCts;

        public bool IsRunning { get; private set; } = false;

        public event Action<string>? LogReceived;
        public event Action<TrafficStats>? TrafficUpdated;
        public event Action? UnexpectedExit;

        public async Task<bool> StartAsync(VpnServerProfile server, VpnSettings settings)
        {
            if (IsRunning) await StopAsync(settings);

            KillOrphanCoreProcesses();

            try
            {
                var coreExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sing-box.exe");

                if (!File.Exists(coreExePath))
                {
                    LogReceived?.Invoke("[VPN CORE] Sing-Box engine binary not found. Attempting automatic download...");
                    var downloaded = await CoreDownloaderService.DownloadSingBoxCoreAsync(msg => LogReceived?.Invoke(msg));
                    if (!downloaded)
                    {
                        LogReceived?.Invoke("[VPN ERROR] Cannot connect without core engine. Please check internet connection or download sing-box.exe into app folder.");
                        return false;
                    }
                }

                var jsonConfig = SingBoxConfigBuilder.BuildJsonConfig(server, settings);
                var configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

                await File.WriteAllTextAsync(configPath, jsonConfig, new UTF8Encoding(false));

                LogReceived?.Invoke($"[VPN CORE] Generated Sing-Box configuration for node: {server.Name} ({server.Protocol.ToUpper()})");
                LogReceived?.Invoke($"[VPN CORE] Config path: {configPath}");

                bool isTunMode = settings.EnableTun || settings.ConnectionMode == "TUN" || settings.ConnectionMode == "Both";
                bool needsAdmin = isTunMode && !IsAdministrator();

                var psi = new ProcessStartInfo
                {
                    FileName = coreExePath,
                    Arguments = $"run -c \"{configPath}\"",
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                if (needsAdmin)
                {
                    LogReceived?.Invoke("[VPN WARNING] TUN mode requires Administrator privileges on Windows to create virtual network adapter.");
                    LogReceived?.Invoke("[VPN INFO] Elevating sing-box core process via UAC prompt...");
                    psi.UseShellExecute = true;
                    psi.Verb = "runas";
                    psi.CreateNoWindow = true;
                    psi.WindowStyle = ProcessWindowStyle.Hidden;
                }
                else
                {
                    psi.UseShellExecute = false;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.CreateNoWindow = true;
                    psi.EnvironmentVariables["ENABLE_DEPRECATED_SPECIAL_OUTBOUNDS"] = "true";
                    psi.EnvironmentVariables["ENABLE_DEPRECATED_TUN_ADDRESS_X"] = "true";
                }

                _coreProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (!needsAdmin)
                {
                    _coreProcess.OutputDataReceived += (s, e) => { if (e.Data != null) LogReceived?.Invoke(e.Data); };
                    _coreProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) LogReceived?.Invoke(e.Data); };
                }

                _coreProcess.Exited += (s, e) =>
                {
                    if (IsRunning)
                    {
                        LogReceived?.Invoke("[VPN CORE] Sing-Box process terminated unexpectedly.");
                        IsRunning = false;
                        UnexpectedExit?.Invoke();
                    }
                };

                _coreProcess.Start();
                if (!needsAdmin)
                {
                    _coreProcess.BeginOutputReadLine();
                    _coreProcess.BeginErrorReadLine();
                }

                await Task.Delay(1200);
                if (_coreProcess.HasExited)
                {
                    LogReceived?.Invoke($"[VPN ERROR] Sing-Box core process exited unexpectedly with code {_coreProcess.ExitCode}. Connection failed.");
                    IsRunning = false;
                    return false;
                }

                if (settings.EnableSysProxy || settings.ConnectionMode == "SysProxy" || settings.ConnectionMode == "Both")
                {
                    SysProxyService.SetSystemProxy("127.0.0.1", settings.InboundHttpPort, settings.InboundSocksPort);
                    LogReceived?.Invoke($"[SYSTEM PROXY] Enabled System Proxy -> HTTP: 127.0.0.1:{settings.InboundHttpPort}, SOCKS: 127.0.0.1:{settings.InboundSocksPort}");
                }

                IsRunning = true;
                StartTrafficMonitoring();
                return true;
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[VPN ERROR] Failed to start core engine: {ex.Message}");
                IsRunning = false;
                return false;
            }
        }

        public async Task StopAsync(VpnSettings settings)
        {
            try
            {
                IsRunning = false;
                _statsCts?.Cancel();

                if (_coreProcess != null)
                {
                    try
                    {
                        if (!_coreProcess.HasExited)
                        {
                            _coreProcess.Kill(true);
                            await _coreProcess.WaitForExitAsync();
                        }
                        _coreProcess.Dispose();
                    }
                    catch { }
                    _coreProcess = null;
                }

                KillOrphanCoreProcesses();

                if (settings.EnableSysProxy)
                {
                    SysProxyService.ClearSystemProxy();
                    LogReceived?.Invoke("[SYSTEM PROXY] Cleared System Proxy settings.");
                }

                LogReceived?.Invoke("[VPN CORE] Engine stopped gracefully.");
            }
            catch (Exception ex)
            {
                LogReceived?.Invoke($"[VPN ERROR] Error during core stop: {ex.Message}");
            }
        }

        private static void KillOrphanCoreProcesses()
        {
            try
            {
                var processes = Process.GetProcessesByName("sing-box");
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill(true);
                        p.WaitForExit(1000);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static readonly System.Net.Http.HttpClient TrafficClient = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };

        private void StartTrafficMonitoring()
        {
            _statsCts = new CancellationTokenSource();
            var token = _statsCts.Token;

            Task.Run(async () =>
            {
                long totalUp = 0;
                long totalDown = 0;

                while (!token.IsCancellationRequested && IsRunning)
                {
                    try
                    {
                        using var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "http://127.0.0.1:9090/traffic");
                        using var response = await TrafficClient.SendAsync(request, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, token);
                        if (response.IsSuccessStatusCode)
                        {
                            using var stream = await response.Content.ReadAsStreamAsync(token);
                            using var reader = new StreamReader(stream);

                            string? line;
                            while ((line = await reader.ReadLineAsync()) != null && !token.IsCancellationRequested && IsRunning)
                            {
                                if (string.IsNullOrWhiteSpace(line)) continue;

                                try
                                {
                                    using var doc = System.Text.Json.JsonDocument.Parse(line);
                                    var root = doc.RootElement;
                                    long up = root.TryGetProperty("up", out var u) ? u.GetInt64() : 0;
                                    long down = root.TryGetProperty("down", out var d) ? d.GetInt64() : 0;

                                    totalUp += up;
                                    totalDown += down;

                                    TrafficUpdated?.Invoke(new TrafficStats
                                    {
                                        UploadSpeedBps = up,
                                        DownloadSpeedBps = down,
                                        TotalUploadBytes = totalUp,
                                        TotalDownloadBytes = totalDown
                                    });
                                }
                                catch { }
                            }
                        }
                    }
                    catch
                    {
                        // Ignore monitoring connection drop retries safely
                    }

                    await Task.Delay(1000, token);
                }
            }, token);
        }

        private static bool IsAdministrator()
        {
            try
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }
    }
}
