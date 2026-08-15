using System;
using System.Collections.Generic;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KeyMapper
{
    public partial class VpnServerProfile : ObservableObject
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
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PingDisplay))]
        [NotifyPropertyChangedFor(nameof(PingColorBrush))]
        [NotifyPropertyChangedFor(nameof(PingBackgroundBrush))]
        private int _pingMs = -1; // -1 means untested, 9999 means timeout

        public string CountryCode2Letter { get; set; } = "un";
        
        private string _flag = "🌐";
        public string Flag
        {
            get
            {
                if (!string.IsNullOrEmpty(_flag) && _flag != "🌐")
                    return _flag;

                var extracted = ExtractFlagEmoji(Name);
                if (!string.IsNullOrEmpty(extracted))
                    return extracted;

                string cc = !string.IsNullOrEmpty(CountryCode2Letter) && !CountryCode2Letter.Equals("un", StringComparison.OrdinalIgnoreCase)
                    ? CountryCode2Letter
                    : DetectCountryCode(Name, Address);

                if (!string.IsNullOrEmpty(cc) && cc.Length == 2 && !cc.Equals("un", StringComparison.OrdinalIgnoreCase))
                {
                    return CountryCodeToFlagEmoji(cc);
                }

                return "🌐";
            }
            set => _flag = value;
        }

        public static string ExtractFlagEmoji(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            for (int i = 0; i < text.Length - 1; i++)
            {
                int codePoint = char.ConvertToUtf32(text, i);
                if (codePoint >= 0x1F1E6 && codePoint <= 0x1F1FF)
                {
                    if (char.IsSurrogatePair(text, i) && i + 3 < text.Length)
                    {
                        int nextCodePoint = char.ConvertToUtf32(text, i + 2);
                        if (nextCodePoint >= 0x1F1E6 && nextCodePoint <= 0x1F1FF)
                        {
                            return text.Substring(i, 4);
                        }
                    }
                }
                if (char.IsSurrogatePair(text, i)) i++;
            }
            return string.Empty;
        }

        public static string CountryCodeToFlagEmoji(string countryCode)
        {
            if (string.IsNullOrEmpty(countryCode) || countryCode.Length != 2) return "🌐";
            countryCode = countryCode.ToUpperInvariant();
            if (countryCode == "UN") return "🌐";

            try
            {
                int firstChar = 0x1F1E6 + (countryCode[0] - 'A');
                int secondChar = 0x1F1E6 + (countryCode[1] - 'A');
                return char.ConvertFromUtf32(firstChar) + char.ConvertFromUtf32(secondChar);
            }
            catch
            {
                return "🌐";
            }
        }

        public static string DetectCountryCode(string name, string address)
        {
            // 1. Try to extract country code from regional flag emoji in the Name
            var flagEmoji = ExtractFlagEmoji(name);
            if (!string.IsNullOrEmpty(flagEmoji) && flagEmoji.Length >= 4)
            {
                try
                {
                    int cp1 = char.ConvertToUtf32(flagEmoji, 0);
                    int cp2 = char.ConvertToUtf32(flagEmoji, 2);
                    if (cp1 >= 0x1F1E6 && cp1 <= 0x1F1FF && cp2 >= 0x1F1E6 && cp2 <= 0x1F1FF)
                    {
                        char c1 = (char)('A' + (cp1 - 0x1F1E6));
                        char c2 = (char)('A' + (cp2 - 0x1F1E6));
                        return $"{c1}{c2}".ToUpperInvariant();
                    }
                }
                catch { }
            }

            string nameUpper = (name ?? "").ToUpperInvariant();

            // 2. High Priority: Detect Country from Config NAME first (before checking address / client ISP)
            // Remove common ISP / network provider keywords that contain "IR" or "IRAN" like "IRANCELL", "IRAN CELL" from matching
            string nameForDetection = System.Text.RegularExpressions.Regex.Replace(nameUpper, @"\b(IRANCELL|IRAN\s*CELL|HAMRAH|MCI|RIGHTEL|SHATEL|ASIATECH|MOKHABERAT|DIRECT|VLESS|VMESS|TROJAN|SS|SHADOWSOCKS)\b", " ");

            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(DE|GERMANY|GERMAN|FRANKFURT|BERLIN|NUREMBERG|DUSSELDORF)\b")) return "DE";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(IT|ITALY|ITALIAN|MILAN|MILANO|ROME|ROMA)\b")) return "IT";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(FR|FRANCE|FRENCH|PARIS|MARSEILLE|LYON|STRASBOURG)\b")) return "FR";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(NL|NETHERLANDS|DUTCH|AMSTERDAM|ROTTERDAM|HAARLEM)\b")) return "NL";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(US|USA|AMERICA|AMERICAN|UNITED STATES|NEW YORK|LOS ANGELES|DALLAS|MIAMI|SEATTLE|CHICAGO|CALIFORNIA|ASHBURN|SILICON)\b")) return "US";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(GB|UK|ENGLAND|ENGLISH|LONDON|MANCHESTER|UNITED KINGDOM|BRITAIN|BRITISH)\b")) return "GB";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(TR|TURKEY|TURKISH|ISTANBUL|ANKARA|BURSA|IZMIR)\b")) return "TR";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(FI|FINLAND|FINNISH|HELSINKI)\b")) return "FI";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(JP|JAPAN|JAPANESE|TOKYO|OSAKA)\b")) return "JP";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(CA|CANADA|CANADIAN|TORONTO|VANCOUVER|MONTREAL)\b")) return "CA";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(SG|SINGAPORE)\b")) return "SG";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(AE|UAE|DUBAI|ABU DHABI)\b")) return "AE";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(SE|SWEDEN|SWEDISH|STOCKHOLM)\b")) return "SE";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(CH|SWITZERLAND|SWISS|ZURICH|GENEVA)\b")) return "CH";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(ES|SPAIN|SPANISH|MADRID|BARCELONA)\b")) return "ES";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(PL|POLAND|POLISH|WARSAW)\b")) return "PL";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(RU|RUSSIA|RUSSIAN|MOSCOW|SAINT PETERSBURG)\b")) return "RU";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(UA|UKRAINE|UKRAINIAN|KYIV)\b")) return "UA";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(AU|AUSTRALIA|AUSTRALIAN|SYDNEY|MELBOURNE)\b")) return "AU";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(KR|KOREA|KOREAN|SEOUL)\b")) return "KR";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(HK|HONG KONG|HONGKONG)\b")) return "HK";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(TW|TAIWAN|TAIPEI)\b")) return "TW";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(NO|NORWAY|NORWEGIAN|OSLO)\b")) return "NO";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(AT|AUSTRIA|AUSTRIAN|VIENNA)\b")) return "AT";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(RO|ROMANIA|ROMANIAN|BUCHAREST)\b")) return "RO";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(BR|BRAZIL|BRAZILIAN|SAO PAULO)\b")) return "BR";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(IN|INDIA|INDIAN|MUMBAI|DELHI|BANGALORE)\b")) return "IN";
            if (System.Text.RegularExpressions.Regex.IsMatch(nameForDetection, @"\b(IR|IRAN|TEHRAN|SHIRAZ|ISFAHAN|TABRIZ|MASHHAD|MELLI|INTRANET)\b")) return "IR";

            // 3. Address Domain-based fallback
            string addressUpper = (address ?? "").ToUpperInvariant();
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(DE|FRANKFURT|BERLIN)\.")) return "DE";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(IT|MILAN|ROMA)\.")) return "IT";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(FR|PARIS)\.")) return "FR";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(NL|AMSTERDAM)\.")) return "NL";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(US|USA)\.")) return "US";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(UK|GB|LONDON)\.")) return "GB";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(TR|ISTANBUL)\.")) return "TR";
            if (System.Text.RegularExpressions.Regex.IsMatch(addressUpper, @"(^|\.)(FI|HELSINKI)\.")) return "FI";

            return "UN";
        }

        public string SubscriptionName { get; set; } = "Manual";
        public string RawUri { get; set; } = "";
        public DateTime LastTested { get; set; } = DateTime.MinValue;

        [ObservableProperty]
        private bool _isActive = false;

        public string EffectiveCountryCode
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(CountryCode2Letter) && !CountryCode2Letter.Equals("un", StringComparison.OrdinalIgnoreCase))
                    return CountryCode2Letter.ToLowerInvariant();

                string detected = DetectCountryCode(Name, Address);
                if (!string.IsNullOrWhiteSpace(detected) && !detected.Equals("un", StringComparison.OrdinalIgnoreCase))
                    return detected.ToLowerInvariant();

                return "un";
            }
        }

        public string CountryCodeDisplay => EffectiveCountryCode.ToUpperInvariant();
        public string FlagImageUrl => $"https://flagcdn.com/w40/{EffectiveCountryCode}.png";

        public string CleanName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return "";
                string n = Name.Trim();

                // Remove leading flag/emoji symbols if present. The previous
                // surrogate-pair character range was invalid in .NET Regex and
                // crashed the Edge Panel while resolving a server display name.
                n = System.Text.RegularExpressions.Regex.Replace(n, @"^\p{So}+\s*", "");

                // Strip redundant 2-letter uppercase country code prefix like "FR ", "FI ", "AE ", "NL-", "IT "
                var cleaned = System.Text.RegularExpressions.Regex.Replace(n, @"^([A-Za-z]{2})\s*[-_|\/:]*\s*", "");
                if (!string.IsNullOrWhiteSpace(cleaned) && cleaned.Length >= 2)
                {
                    return cleaned;
                }

                return n;
            }
        }

        public string PingDisplay => PingMs < 0 ? "Untested" : (PingMs >= 9999 ? "Timeout" : $"{PingMs} ms");
        public string ProtocolBadge => Protocol.ToUpperInvariant();
        public string AddressDisplay => $"{Address}:{Port}";

        public Brush PingColorBrush
        {
            get
            {
                if (PingMs < 0) return new SolidColorBrush(Color.FromArgb(220, 148, 163, 184)); // Slate 400
                if (PingMs >= 9999) return new SolidColorBrush(Color.FromArgb(230, 239, 68, 68)); // Red 500
                if (PingMs < 150) return new SolidColorBrush(Color.FromArgb(240, 16, 185, 129)); // Emerald 500
                if (PingMs < 350) return new SolidColorBrush(Color.FromArgb(240, 245, 158, 11)); // Amber 500
                return new SolidColorBrush(Color.FromArgb(230, 239, 68, 68)); // Red 500
            }
        }

        public Brush PingBackgroundBrush
        {
            get
            {
                if (PingMs < 0) return new SolidColorBrush(Color.FromArgb(30, 148, 163, 184));
                if (PingMs >= 9999) return new SolidColorBrush(Color.FromArgb(35, 239, 68, 68));
                if (PingMs < 150) return new SolidColorBrush(Color.FromArgb(40, 16, 185, 129));
                if (PingMs < 350) return new SolidColorBrush(Color.FromArgb(40, 245, 158, 11));
                return new SolidColorBrush(Color.FromArgb(35, 239, 68, 68));
            }
        }
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
