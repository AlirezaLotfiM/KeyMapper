using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KeyMapper
{
    public class VpnServerProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New Server";
        public string Protocol { get; set; } = "vless"; // vless, vmess, trojan, ss, hysteria2, tuic
        public string Address { get; set; } = "";
        public int Port { get; set; } = 443;

        // Credentials / Auth
        public string Uuid { get; set; } = ""; // UUID for vless/vmess
        public string Password { get; set; } = ""; // Password for trojan/ss/hy2
        public string Security { get; set; } = "tls"; // tls, reality, none
        public string Sni { get; set; } = "";
        public string Alpn { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string PublicKey { get; set; } = ""; // Reality public key
        public string ShortId { get; set; } = ""; // Reality short id
        public string Flow { get; set; } = ""; // xtls-rprx-vision

        // Transport
        public string Transport { get; set; } = "tcp"; // tcp, ws, grpc, splithttp, h2
        public string Path { get; set; } = "";
        public string Host { get; set; } = "";
        public string HeaderType { get; set; } = "";

        // Status & Metadata
        public int PingMs { get; set; } = -1; // -1 means untested, 9999 means timeout
        public string CountryCode2Letter { get; set; } = "un";
        public string Flag { get; set; } = "🌐";
        public string SubscriptionName { get; set; } = "Manual";
        public string RawUri { get; set; } = "";
        public DateTime LastTested { get; set; } = DateTime.MinValue;
        public bool IsActive { get; set; } = false;

        public string FlagImageUrl => $"https://flagcdn.com/w40/{CountryCode2Letter.ToLowerInvariant()}.png";
        public string PingDisplay => PingMs < 0 ? "Untested" : (PingMs >= 9999 ? "Timeout" : $"{PingMs} ms");
        public string ProtocolBadge => Protocol.ToUpperInvariant();
        public string AddressDisplay => $"{Address}:{Port}";
    }

    public class VpnSubscription
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "My Subscription";
        public string Url { get; set; } = "";
        public bool AutoUpdate { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.MinValue;
        public int NodeCount { get; set; } = 0;

        // Traffic info headers parsed from feed
        public long DownloadBytes { get; set; } = 0;
        public long UploadBytes { get; set; } = 0;
        public long TotalBytes { get; set; } = 0;
        public long ExpireTimestamp { get; set; } = 0;

        public double UsedGb => Math.Round((DownloadBytes + UploadBytes) / (1024.0 * 1024.0 * 1024.0), 2);
        public double TotalGb => TotalBytes > 0 ? Math.Round(TotalBytes / (1024.0 * 1024.0 * 1024.0), 2) : 0;
        public double UsagePercentage => TotalBytes > 0 ? Math.Min(100, Math.Max(0, Math.Round(((double)(DownloadBytes + UploadBytes) / TotalBytes) * 100, 1))) : 0;

        public string NodeCountDisplay => $"{NodeCount} Nodes";
        public string UsageDisplay => TotalBytes > 0 ? $"{UsedGb} GB / {TotalGb} GB" : "Unlimited";
        public string ExpiryDisplay => ExpireTimestamp > 0
            ? DateTimeOffset.FromUnixTimeSeconds(ExpireTimestamp).LocalDateTime.ToString("yyyy-MM-dd")
            : "No Expiry";
    }

    public class VpnSettings
    {
        public int InboundHttpPort { get; set; } = 2080;
        public int InboundSocksPort { get; set; } = 2081;
        public bool AllowLan { get; set; } = false;
        public string ConnectionMode { get; set; } = "SysProxy"; // SysProxy, TUN, Both
        public string RoutingMode { get; set; } = "Rule"; // Rule (Bypass CN/IR/LAN), Global, Direct
        public bool EnableSysProxy { get; set; } = true;
        public bool EnableTun { get; set; } = false;
        public string TunStack { get; set; } = "mixed"; // mixed, gvisor, system
        public string PerAppMode { get; set; } = "Disabled"; // Disabled, Include, Exclude
        public List<string> SelectedPerAppProcesses { get; set; } = new List<string>();
        public string ActiveServerId { get; set; } = "";
        public string DnsServer { get; set; } = "8.8.8.8";
    }

    public class TrafficStats
    {
        public long UploadSpeedBps { get; set; }
        public long DownloadSpeedBps { get; set; }
        public long TotalUploadBytes { get; set; }
        public long TotalDownloadBytes { get; set; }

        public string UploadSpeedDisplay => FormatSpeed(UploadSpeedBps);
        public string DownloadSpeedDisplay => FormatSpeed(DownloadSpeedBps);
        public string TotalUploadDisplay => FormatBytes(TotalUploadBytes);
        public string TotalDownloadDisplay => FormatBytes(TotalDownloadBytes);

        private static string FormatSpeed(long bps)
        {
            if (bps < 1024) return $"{bps} B/s";
            if (bps < 1024 * 1024) return $"{bps / 1024.0:F1} KB/s";
            if (bps < 1024 * 1024 * 1024) return $"{bps / (1024.0 * 1024.0):F1} MB/s";
            return $"{bps / (1024.0 * 1024.0 * 1024.0):F2} GB/s";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F2} MB";
            return $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
        }
    }

    public partial class AppProcessItem : ObservableObject
    {
        [ObservableProperty]
        private string _processName = "";

        [ObservableProperty]
        private string _displayName = "";

        [ObservableProperty]
        private string _executablePath = "";

        [ObservableProperty]
        private ImageSource? _icon;

        [ObservableProperty]
        private bool _isSelected = false;
    }

    public class VpnServerGroup
    {
        public string GroupName { get; set; } = "Manual / Custom Nodes";
        public bool IsSubscription { get; set; } = false;
        public VpnSubscription? Subscription { get; set; }
        public List<VpnServerProfile> Servers { get; set; } = new List<VpnServerProfile>();

        public string TrafficSummaryText
        {
            get
            {
                if (Subscription == null) return $"{Servers.Count} Nodes";
                if (Subscription.TotalBytes > 0)
                {
                    double remaining = Math.Max(0, Subscription.TotalGb - Subscription.UsedGb);
                    return $"{Subscription.UsedGb} GB / {Subscription.TotalGb} GB ({remaining:F1} GB remaining)";
                }
                return $"{Subscription.UsedGb} GB used (Unlimited)";
            }
        }

        public string DaysLeftText
        {
            get
            {
                if (Subscription == null || Subscription.ExpireTimestamp <= 0) return "";
                var expireDate = DateTimeOffset.FromUnixTimeSeconds(Subscription.ExpireTimestamp).LocalDateTime;
                var daysLeft = (int)Math.Ceiling((expireDate - DateTime.Now).TotalDays);
                if (daysLeft < 0) return "Expired";
                if (daysLeft == 0) return "Expires today";
                return $"{daysLeft} days left ({expireDate:yyyy-MM-dd})";
            }
        }
    }
}
