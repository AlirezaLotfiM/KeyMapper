using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace KeyMapper
{
    public static class SpeedTestService
    {
        public static async Task<int> TestProxyHttpDelayAsync(int proxyPort = 2080, int timeoutMs = 3000)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    UseProxy = true,
                    Proxy = new WebProxy($"127.0.0.1:{proxyPort}")
                };
                using var client = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(timeoutMs) };

                var sw = Stopwatch.StartNew();
                var response = await client.GetAsync("http://cp.cloudflare.com/generate_204");
                sw.Stop();

                if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                {
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch { }

            return 9999;
        }

        public static async Task<int> TestServerRealDelayAsync(VpnServerProfile server, int httpProxyPort = 2080, bool isCurrentlyConnectedServer = false, int timeoutMs = 3000)
        {
            if (string.IsNullOrWhiteSpace(server.Address)) return 9999;

            // 1. If this is the active connected server, test real HTTP end-to-end delay through local proxy
            if (isCurrentlyConnectedServer && VpnService.Instance.IsConnected)
            {
                int proxyDelay = await TestProxyHttpDelayAsync(httpProxyPort, timeoutMs);
                if (proxyDelay < 9999) return proxyDelay;
            }

            // 2. Measure actual TCP Handshake Latency (Syn -> SynAck RTT)
            int tcpRtt = 9999;
            try
            {
                using var tcpClient = new TcpClient();
                using var cts = new CancellationTokenSource(timeoutMs);

                var sw = Stopwatch.StartNew();
                var connectTask = tcpClient.ConnectAsync(server.Address, server.Port);

                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs, cts.Token)) != connectTask || !tcpClient.Connected)
                {
                    return 9999;
                }

                sw.Stop();
                tcpRtt = Math.Max(1, (int)sw.ElapsedMilliseconds);

                // If standard TLS is used (not reality or UDP-based), optionally test full TLS handshake
                bool isStandardTls = server.Security.Equals("tls", StringComparison.OrdinalIgnoreCase) ||
                                     server.Protocol.Equals("trojan", StringComparison.OrdinalIgnoreCase);

                if (isStandardTls)
                {
                    try
                    {
                        string sniHost = !string.IsNullOrWhiteSpace(server.Sni)
                            ? server.Sni
                            : (!string.IsNullOrWhiteSpace(server.Host) ? server.Host : server.Address);

                        using var stream = tcpClient.GetStream();
                        stream.ReadTimeout = 1500;
                        stream.WriteTimeout = 1500;

                        using var sslStream = new SslStream(stream, false, (s, cert, chain, errs) => true);
                        var tlsSw = Stopwatch.StartNew();
                        var sslTask = sslStream.AuthenticateAsClientAsync(sniHost);

                        if (await Task.WhenAny(sslTask, Task.Delay(1500)) == sslTask && sslStream.IsAuthenticated)
                        {
                            tlsSw.Stop();
                            return Math.Max(1, (int)tlsSw.ElapsedMilliseconds);
                        }
                    }
                    catch
                    {
                        // Fallback to TCP RTT if TLS handshake fails
                        return tcpRtt;
                    }
                }

                return tcpRtt;
            }
            catch
            {
                return 9999;
            }
        }

        public static async Task TestServerAsync(VpnServerProfile server)
        {
            bool isConnectedNode = VpnService.Instance.IsConnected && VpnService.Instance.ActiveServer?.Id == server.Id;
            int delay = await TestServerRealDelayAsync(server, VpnService.Instance.Settings.InboundHttpPort, isConnectedNode);
            server.PingMs = delay;
            server.LastTested = DateTime.Now;
        }
    }
}
