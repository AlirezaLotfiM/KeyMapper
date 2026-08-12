using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;

namespace KeyMapper
{
    public static class SpeedTestService
    {
        public static async Task<int> TestProxyHttpDelayAsync(int proxyPort = 2080, int timeoutMs = 3500)
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
                var response = await client.GetAsync("http://www.gstatic.com/generate_204");
                sw.Stop();

                if (response.IsSuccessStatusCode || (int)response.StatusCode == 204)
                {
                    return (int)sw.ElapsedMilliseconds;
                }
            }
            catch { }

            return 9999;
        }

        public static async Task<int> TestServerRealDelayAsync(VpnServerProfile server, int httpProxyPort = 2080, bool isCurrentlyConnectedServer = false, int timeoutMs = 3500)
        {
            if (string.IsNullOrWhiteSpace(server.Address)) return 9999;

            // 1. If this is the active connected server, test real HTTP end-to-end delay through local proxy
            if (isCurrentlyConnectedServer && VpnService.Instance.IsConnected)
            {
                int proxyDelay = await TestProxyHttpDelayAsync(httpProxyPort, timeoutMs);
                if (proxyDelay < 9999) return proxyDelay;
            }

            // 2. Real Handshake Delay (TLS ClientHello/ServerHello Handshake or TCP Connect RTT)
            try
            {
                var sw = Stopwatch.StartNew();
                using var tcpClient = new TcpClient();

                var connectTask = tcpClient.ConnectAsync(server.Address, server.Port);
                if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask || !tcpClient.Connected)
                {
                    return 9999;
                }

                string sniHost = !string.IsNullOrWhiteSpace(server.Sni) 
                    ? server.Sni 
                    : (!string.IsNullOrWhiteSpace(server.Host) ? server.Host : server.Address);

                bool isTls = server.Security == "tls" ||
                             server.Security == "reality" ||
                             server.Protocol.Equals("trojan", StringComparison.OrdinalIgnoreCase) ||
                             server.Protocol.Equals("hysteria2", StringComparison.OrdinalIgnoreCase) ||
                             server.Protocol.Equals("hy2", StringComparison.OrdinalIgnoreCase) ||
                             server.Protocol.Equals("tuic", StringComparison.OrdinalIgnoreCase);

                if (isTls)
                {
                    using var stream = tcpClient.GetStream();
                    stream.ReadTimeout = timeoutMs;
                    stream.WriteTimeout = timeoutMs;

                    using var sslStream = new SslStream(stream, false, (sender, cert, chain, errors) => true);
                    var sslTask = sslStream.AuthenticateAsClientAsync(sniHost);
                    
                    if (await Task.WhenAny(sslTask, Task.Delay(timeoutMs)) == sslTask)
                    {
                        sw.Stop();
                        return (int)sw.ElapsedMilliseconds;
                    }
                    return 9999;
                }
                else
                {
                    sw.Stop();
                    return (int)sw.ElapsedMilliseconds;
                }
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
