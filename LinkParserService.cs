using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace KeyMapper
{
    public static class LinkParserService
    {
        public static List<VpnServerProfile> ParseContent(string content, string subscriptionName = "Manual")
        {
            var results = new List<VpnServerProfile>();
            if (string.IsNullOrWhiteSpace(content)) return results;

            var trimmed = content.Trim();
            if (trimmed.StartsWith("{") || trimmed.StartsWith("["))
            {
                var jsonNodes = ParseJsonConfig(trimmed, subscriptionName);
                if (jsonNodes.Count > 0) return jsonNodes;
            }

            var decodedStr = TryBase64Decode(trimmed);
            var textToParse = string.IsNullOrWhiteSpace(decodedStr) ? content : decodedStr;

            var lines = textToParse.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line)) continue;

                if (line.StartsWith("{") || line.StartsWith("["))
                {
                    var jNodes = ParseJsonConfig(line, subscriptionName);
                    results.AddRange(jNodes);
                    continue;
                }

                var profile = ParseSingleUri(line, subscriptionName);
                if (profile != null)
                {
                    results.Add(profile);
                }
            }

            return results;
        }

        public static List<VpnServerProfile> ParseJsonConfig(string jsonStr, string subName = "Manual")
        {
            var list = new List<VpnServerProfile>();
            try
            {
                using var doc = JsonDocument.Parse(jsonStr);
                var root = doc.RootElement;

                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("outbounds", out var outbounds) && outbounds.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var element in outbounds.EnumerateArray())
                        {
                            var p = ParseSingleJsonOutbound(element, subName);
                            if (p != null) list.Add(p);
                        }
                    }
                    else
                    {
                        var p = ParseSingleJsonOutbound(root, subName);
                        if (p != null) list.Add(p);
                    }
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    foreach (var element in root.EnumerateArray())
                    {
                        var p = ParseSingleJsonOutbound(element, subName);
                        if (p != null) list.Add(p);
                    }
                }
            }
            catch { }
            return list;
        }

        private static VpnServerProfile? ParseSingleJsonOutbound(JsonElement el, string subName)
        {
            if (el.ValueKind != JsonValueKind.Object) return null;

            string type = el.TryGetProperty("type", out var tProp) ? tProp.GetString()?.ToLowerInvariant() ?? "" : "";
            if (type == "direct" || type == "block" || type == "dns" || string.IsNullOrEmpty(type)) return null;

            string tag = el.TryGetProperty("tag", out var tagProp) ? tagProp.GetString() ?? $"{type.ToUpper()} Node" : $"{type.ToUpper()} Node";
            string server = el.TryGetProperty("server", out var sProp) ? sProp.GetString() ?? "" : "";
            int port = el.TryGetProperty("server_port", out var pProp) ? (pProp.ValueKind == JsonValueKind.Number ? pProp.GetInt32() : 443) : 443;
            if (string.IsNullOrWhiteSpace(server)) return null;

            string uuid = el.TryGetProperty("uuid", out var uProp) ? uProp.GetString() ?? "" : "";
            string pass = el.TryGetProperty("password", out var pwProp) ? pwProp.GetString() ?? "" : "";

            string sec = "none";
            string sni = "";
            string alpn = "";
            string fp = "";
            string pbk = "";
            string sid = "";

            if (el.TryGetProperty("tls", out var tlsObj) && tlsObj.ValueKind == JsonValueKind.Object)
            {
                sec = "tls";
                if (tlsObj.TryGetProperty("server_name", out var sniProp)) sni = sniProp.GetString() ?? "";
                if (tlsObj.TryGetProperty("utls", out var utlsObj) && utlsObj.ValueKind == JsonValueKind.Object)
                {
                    if (utlsObj.TryGetProperty("fingerprint", out var fpProp)) fp = fpProp.GetString() ?? "";
                }
                if (tlsObj.TryGetProperty("reality", out var rObj) && rObj.ValueKind == JsonValueKind.Object)
                {
                    sec = "reality";
                    if (rObj.TryGetProperty("public_key", out var pbkProp)) pbk = pbkProp.GetString() ?? "";
                    if (rObj.TryGetProperty("short_id", out var sidProp)) sid = sidProp.GetString() ?? "";
                }
            }

            string transport = "tcp";
            string path = "";
            string host = "";
            if (el.TryGetProperty("transport", out var trObj) && trObj.ValueKind == JsonValueKind.Object)
            {
                transport = trObj.TryGetProperty("type", out var trType) ? trType.GetString() ?? "tcp" : "tcp";
                if (trObj.TryGetProperty("path", out var pathProp)) path = pathProp.GetString() ?? "";
            }

            var cc = DetectCountryCode(tag, server);

            return new VpnServerProfile
            {
                Name = tag,
                Protocol = type,
                Address = server,
                Port = port,
                Uuid = uuid,
                Password = pass,
                Security = sec,
                Sni = sni,
                Alpn = alpn,
                Fingerprint = fp,
                PublicKey = pbk,
                ShortId = sid,
                Transport = transport,
                Path = path,
                Host = host,
                SubscriptionName = subName,
                CountryCode2Letter = cc
            };
        }

        public static VpnServerProfile? ParseSingleUri(string uri, string subscriptionName = "Manual")
        {
            if (string.IsNullOrWhiteSpace(uri)) return null;
            uri = uri.Trim();

            try
            {
                if (uri.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                    return ParseVless(uri, subscriptionName);
                if (uri.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                    return ParseVmess(uri, subscriptionName);
                if (uri.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                    return ParseTrojan(uri, subscriptionName);
                if (uri.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                    return ParseShadowsocks(uri, subscriptionName);
                if (uri.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("hy2://", StringComparison.OrdinalIgnoreCase))
                    return ParseHysteria2(uri, subscriptionName);
                if (uri.StartsWith("tuic://", StringComparison.OrdinalIgnoreCase))
                    return ParseTuic(uri, subscriptionName);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to parse link {uri}: {ex.Message}");
            }

            return null;
        }

        private static bool TryParseHostPort(string hostPort, out string host, out int port)
        {
            host = "";
            port = 443;
            if (string.IsNullOrWhiteSpace(hostPort)) return false;

            var lastColon = hostPort.LastIndexOf(':');
            if (lastColon <= 0 || lastColon == hostPort.Length - 1) return false;

            if (!int.TryParse(hostPort.Substring(lastColon + 1), out port)) return false;

            host = hostPort.Substring(0, lastColon).Trim('[', ']');
            return !string.IsNullOrWhiteSpace(host);
        }

        private static VpnServerProfile? ParseVless(string uri, string subName)
        {
            var mainUri = uri.Substring(8);
            var name = "VLESS Node";
            var hashIdx = mainUri.IndexOf('#');
            if (hashIdx >= 0)
            {
                name = WebUtility.UrlDecode(mainUri.Substring(hashIdx + 1));
                mainUri = mainUri.Substring(0, hashIdx);
            }

            var queryIdx = mainUri.IndexOf('?');
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (queryIdx >= 0)
            {
                var queryString = mainUri.Substring(queryIdx + 1);
                mainUri = mainUri.Substring(0, queryIdx);
                foreach (var pair in queryString.Split('&'))
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2) queryParams[parts[0]] = WebUtility.UrlDecode(parts[1]);
                }
            }

            var atIdx = mainUri.LastIndexOf('@');
            if (atIdx < 0) return null;

            var uuid = mainUri.Substring(0, atIdx);
            var hostPort = mainUri.Substring(atIdx + 1);
            if (!TryParseHostPort(hostPort, out string address, out int port)) return null;

            var cc = DetectCountryCode(name, address);

            var profile = new VpnServerProfile
            {
                Name = name,
                Protocol = "vless",
                Address = address,
                Port = port,
                Uuid = uuid,
                SubscriptionName = subName,
                RawUri = uri,
                CountryCode2Letter = cc
            };

            if (queryParams.TryGetValue("security", out var sec)) profile.Security = sec;
            if (queryParams.TryGetValue("sni", out var sni)) profile.Sni = sni;
            if (queryParams.TryGetValue("alpn", out var alpn)) profile.Alpn = alpn;
            if (queryParams.TryGetValue("fp", out var fp)) profile.Fingerprint = fp;
            if (queryParams.TryGetValue("type", out var trans)) profile.Transport = trans;
            if (queryParams.TryGetValue("path", out var path)) profile.Path = path;
            if (queryParams.TryGetValue("host", out var host)) profile.Host = host;
            if (queryParams.TryGetValue("flow", out var flow)) profile.Flow = flow;
            if (queryParams.TryGetValue("pbk", out var pbk)) profile.PublicKey = pbk;
            if (queryParams.TryGetValue("sid", out var sid)) profile.ShortId = sid;
            if (queryParams.TryGetValue("headerType", out var ht)) profile.HeaderType = ht;

            return profile;
        }

        private static VpnServerProfile? ParseVmess(string uri, string subName)
        {
            var b64 = uri.Substring(8);
            var jsonStr = TryBase64Decode(b64);
            if (string.IsNullOrWhiteSpace(jsonStr)) return null;

            using var doc = JsonDocument.Parse(jsonStr);
            var root = doc.RootElement;

            string name = root.TryGetProperty("ps", out var psProp) ? psProp.GetString() ?? "VMess Node" : "VMess Node";
            string add = root.TryGetProperty("add", out var addProp) ? addProp.GetString() ?? "" : "";
            int port = root.TryGetProperty("port", out var portProp) ? (portProp.ValueKind == JsonValueKind.Number ? portProp.GetInt32() : int.Parse(portProp.GetString() ?? "443")) : 443;
            string id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
            string net = root.TryGetProperty("net", out var netProp) ? netProp.GetString() ?? "tcp" : "tcp";
            string type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "none" : "none";
            string host = root.TryGetProperty("host", out var hostProp) ? hostProp.GetString() ?? "" : "";
            string path = root.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
            string tls = root.TryGetProperty("tls", out var tlsProp) ? tlsProp.GetString() ?? "" : "";
            string sni = root.TryGetProperty("sni", out var sniProp) ? sniProp.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(add) || string.IsNullOrWhiteSpace(id)) return null;

            var cc = DetectCountryCode(name, add);

            return new VpnServerProfile
            {
                Name = name,
                Protocol = "vmess",
                Address = add.Trim('[', ']'),
                Port = port,
                Uuid = id,
                Transport = net,
                HeaderType = type,
                Host = host,
                Path = path,
                Security = string.IsNullOrWhiteSpace(tls) ? "none" : tls,
                Sni = sni,
                SubscriptionName = subName,
                RawUri = uri,
                CountryCode2Letter = cc
            };
        }

        private static VpnServerProfile? ParseTrojan(string uri, string subName)
        {
            var mainUri = uri.Substring(9);
            var name = "Trojan Node";
            var hashIdx = mainUri.IndexOf('#');
            if (hashIdx >= 0)
            {
                name = WebUtility.UrlDecode(mainUri.Substring(hashIdx + 1));
                mainUri = mainUri.Substring(0, hashIdx);
            }

            var queryIdx = mainUri.IndexOf('?');
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (queryIdx >= 0)
            {
                var queryString = mainUri.Substring(queryIdx + 1);
                mainUri = mainUri.Substring(0, queryIdx);
                foreach (var pair in queryString.Split('&'))
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2) queryParams[parts[0]] = WebUtility.UrlDecode(parts[1]);
                }
            }

            var atIdx = mainUri.LastIndexOf('@');
            if (atIdx < 0) return null;

            var password = mainUri.Substring(0, atIdx);
            var hostPort = mainUri.Substring(atIdx + 1);
            if (!TryParseHostPort(hostPort, out string address, out int port)) return null;

            var cc = DetectCountryCode(name, address);

            var profile = new VpnServerProfile
            {
                Name = name,
                Protocol = "trojan",
                Address = address,
                Port = port,
                Password = password,
                Security = "tls",
                SubscriptionName = subName,
                RawUri = uri,
                CountryCode2Letter = cc
            };

            if (queryParams.TryGetValue("sni", out var sni)) profile.Sni = sni;
            if (queryParams.TryGetValue("alpn", out var alpn)) profile.Alpn = alpn;
            if (queryParams.TryGetValue("type", out var trans)) profile.Transport = trans;
            if (queryParams.TryGetValue("path", out var path)) profile.Path = path;
            if (queryParams.TryGetValue("host", out var host)) profile.Host = host;

            return profile;
        }

        private static VpnServerProfile? ParseShadowsocks(string uri, string subName)
        {
            var mainUri = uri.Substring(5);
            var name = "Shadowsocks Node";
            var hashIdx = mainUri.IndexOf('#');
            if (hashIdx >= 0)
            {
                name = WebUtility.UrlDecode(mainUri.Substring(hashIdx + 1));
                mainUri = mainUri.Substring(0, hashIdx);
            }

            var queryIdx = mainUri.IndexOf('?');
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (queryIdx >= 0)
            {
                var queryString = mainUri.Substring(queryIdx + 1);
                mainUri = mainUri.Substring(0, queryIdx);
                foreach (var pair in queryString.Split('&'))
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2) queryParams[parts[0]] = WebUtility.UrlDecode(parts[1]);
                }
            }

            string userHostPort = mainUri;
            if (!mainUri.Contains("@"))
            {
                var decoded = TryBase64Decode(mainUri);
                if (!string.IsNullOrWhiteSpace(decoded))
                {
                    userHostPort = decoded;
                }
            }

            var atIdx = userHostPort.LastIndexOf('@');
            if (atIdx < 0) return null;

            string userInfo = userHostPort.Substring(0, atIdx);
            string hostPort = userHostPort.Substring(atIdx + 1);

            var decodedUser = TryBase64Decode(userInfo);
            var authStr = string.IsNullOrWhiteSpace(decodedUser) ? userInfo : decodedUser;

            int colonIdx = authStr.IndexOf(':');
            string method = colonIdx >= 0 ? authStr.Substring(0, colonIdx) : "aes-256-gcm";
            string password = colonIdx >= 0 ? authStr.Substring(colonIdx + 1) : authStr;

            if (!TryParseHostPort(hostPort, out string address, out int port)) return null;

            var cc = DetectCountryCode(name, address);

            return new VpnServerProfile
            {
                Name = name,
                Protocol = "ss",
                Address = address,
                Port = port,
                HeaderType = method,
                Password = password,
                SubscriptionName = subName,
                RawUri = uri,
                CountryCode2Letter = cc
            };
        }

        private static VpnServerProfile? ParseHysteria2(string uri, string subName)
        {
            var prefixLen = uri.StartsWith("hysteria2://", StringComparison.OrdinalIgnoreCase) ? 12 : 6;
            var mainUri = uri.Substring(prefixLen);
            var name = "Hysteria 2 Node";
            var hashIdx = mainUri.IndexOf('#');
            if (hashIdx >= 0)
            {
                name = WebUtility.UrlDecode(mainUri.Substring(hashIdx + 1));
                mainUri = mainUri.Substring(0, hashIdx);
            }

            var queryIdx = mainUri.IndexOf('?');
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (queryIdx >= 0)
            {
                var queryString = mainUri.Substring(queryIdx + 1);
                mainUri = mainUri.Substring(0, queryIdx);
                foreach (var pair in queryString.Split('&'))
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2) queryParams[parts[0]] = WebUtility.UrlDecode(parts[1]);
                }
            }

            var atIdx = mainUri.LastIndexOf('@');
            if (atIdx < 0) return null;

            var password = mainUri.Substring(0, atIdx);
            var hostPort = mainUri.Substring(atIdx + 1);
            if (!TryParseHostPort(hostPort, out string address, out int port)) return null;

            var cc = DetectCountryCode(name, address);

            var profile = new VpnServerProfile
            {
                Name = name,
                Protocol = "hysteria2",
                Address = address,
                Port = port,
                Password = password,
                Security = "tls",
                SubscriptionName = subName,
                RawUri = uri,
                CountryCode2Letter = cc
            };

            if (queryParams.TryGetValue("sni", out var sni)) profile.Sni = sni;
            if (queryParams.TryGetValue("obfs", out var obfs)) profile.HeaderType = obfs;

            return profile;
        }

        private static VpnServerProfile? ParseTuic(string uri, string subName)
        {
            var mainUri = uri.Substring(7);
            var name = "TUIC Node";
            var hashIdx = mainUri.IndexOf('#');
            if (hashIdx >= 0)
            {
                name = WebUtility.UrlDecode(mainUri.Substring(hashIdx + 1));
                mainUri = mainUri.Substring(0, hashIdx);
            }

            var queryIdx = mainUri.IndexOf('?');
            var queryParams = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (queryIdx >= 0)
            {
                var queryString = mainUri.Substring(queryIdx + 1);
                mainUri = mainUri.Substring(0, queryIdx);
                foreach (var pair in queryString.Split('&'))
                {
                    var parts = pair.Split('=');
                    if (parts.Length == 2) queryParams[parts[0]] = WebUtility.UrlDecode(parts[1]);
                }
            }

            var atIdx = mainUri.LastIndexOf('@');
            if (atIdx < 0) return null;

            var auth = mainUri.Substring(0, atIdx);
            var hostPort = mainUri.Substring(atIdx + 1);
            if (!TryParseHostPort(hostPort, out string address, out int port)) return null;

            var authParts = auth.Split(':');
            var cc = DetectCountryCode(name, address);

            var profile = new VpnServerProfile
            {
                Name = name,
                Protocol = "tuic",
                Address = address,
                Port = port,
                Uuid = authParts[0],
                Password = authParts.Length > 1 ? authParts[1] : "",
                Security = "tls",
                SubscriptionName = subName,
                RawUri = uri,
                CountryCode2Letter = cc
            };

            if (queryParams.TryGetValue("sni", out var sni)) profile.Sni = sni;
            if (queryParams.TryGetValue("alpn", out var alpn)) profile.Alpn = alpn;

            return profile;
        }

        private static string TryBase64Decode(string input)
        {
            try
            {
                var s = input.Replace('-', '+').Replace('_', '/');
                switch (s.Length % 4)
                {
                    case 2: s += "=="; break;
                    case 3: s += "="; break;
                }
                var bytes = Convert.FromBase64String(s);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string DetectCountryCode(string name, string host)
        {
            var text = (name + " " + host).ToUpperInvariant();
            if (Regex.IsMatch(text, @"\b(DE|GERMANY|FRANKFURT|BERLIN)\b")) return "de";
            if (Regex.IsMatch(text, @"\b(US|USA|AMERICA|NEW YORK|LOS ANGELES|DALLAS)\b")) return "us";
            if (Regex.IsMatch(text, @"\b(GB|UK|ENGLAND|LONDON|MANCHESTER)\b")) return "gb";
            if (Regex.IsMatch(text, @"\b(TR|TURKEY|ISTANBUL|ANKARA)\b")) return "tr";
            if (Regex.IsMatch(text, @"\b(NL|NETHERLANDS|AMSTERDAM)\b")) return "nl";
            if (Regex.IsMatch(text, @"\b(FR|FRANCE|PARIS)\b")) return "fr";
            if (Regex.IsMatch(text, @"\b(FI|FINLAND|HELSINKI)\b")) return "fi";
            if (Regex.IsMatch(text, @"\b(JP|JAPAN|TOKYO|OSAKA)\b")) return "jp";
            if (Regex.IsMatch(text, @"\b(CA|CANADA|TORONTO|VANCOUVER)\b")) return "ca";
            if (Regex.IsMatch(text, @"\b(SG|SINGAPORE)\b")) return "sg";
            if (Regex.IsMatch(text, @"\b(AE|UAE|DUBAI)\b")) return "ae";
            return "un";
        }
    }
}
