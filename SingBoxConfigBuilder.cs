using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace KeyMapper
{
    public static class SingBoxConfigBuilder
    {
        public static string BuildJsonConfig(VpnServerProfile server, VpnSettings settings)
        {
            var root = new JsonObject();
            var listenAddr = settings.AllowLan ? "0.0.0.0" : "127.0.0.1";

            // 1. Log Config
            root["log"] = new JsonObject
            {
                ["level"] = "info",
                ["timestamp"] = true
            };

            // 2. Experimental API (Clash API for Real-time Traffic Monitoring)
            root["experimental"] = new JsonObject
            {
                ["clash_api"] = new JsonObject
                {
                    ["external_controller"] = "127.0.0.1:9090",
                    ["external_ui"] = ""
                }
            };

            // 3. DNS Config
            string remoteDnsIp = "https://8.8.8.8/dns-query";
            string directDnsIp = "https://223.5.5.5/dns-query";

            if (!string.IsNullOrWhiteSpace(settings.DnsServer))
            {
                var customDns = settings.DnsServer.Trim();
                if (customDns.StartsWith("https://") || customDns.StartsWith("tcp://") || customDns.StartsWith("udp://") || IPAddress.TryParse(customDns, out _))
                {
                    remoteDnsIp = customDns;
                }
            }

            var serverDnsRule = new JsonObject { ["server"] = "dns-direct" };
            if (IPAddress.TryParse(server.Address, out _))
            {
                serverDnsRule["ip_cidr"] = new JsonArray { $"{server.Address}/32" };
            }
            else
            {
                serverDnsRule["domain"] = new JsonArray { server.Address };
            }

            root["dns"] = new JsonObject
            {
                ["servers"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["tag"] = "dns-remote",
                        ["address"] = remoteDnsIp,
                        ["detour"] = "proxy"
                    },
                    new JsonObject
                    {
                        ["tag"] = "dns-direct",
                        ["address"] = directDnsIp,
                        ["detour"] = "direct"
                    }
                },
                ["rules"] = new JsonArray
                {
                    serverDnsRule,
                    new JsonObject
                    {
                        ["clash_mode"] = "Direct",
                        ["server"] = "dns-direct"
                    },
                    new JsonObject
                    {
                        ["clash_mode"] = "Global",
                        ["server"] = "dns-remote"
                    }
                },
                ["final"] = "dns-remote",
                ["independent_cache"] = true
            };

            // 4. Inbounds Config
            var inbounds = new JsonArray();

            // HTTP Inbound
            inbounds.Add(new JsonObject
            {
                ["type"] = "http",
                ["tag"] = "http-in",
                ["listen"] = listenAddr,
                ["listen_port"] = settings.InboundHttpPort
            });

            // SOCKS Inbound
            inbounds.Add(new JsonObject
            {
                ["type"] = "socks",
                ["tag"] = "socks-in",
                ["listen"] = listenAddr,
                ["listen_port"] = settings.InboundSocksPort
            });

            // TUN Inbound
            if (settings.EnableTun || settings.ConnectionMode == "TUN" || settings.ConnectionMode == "Both")
            {
                inbounds.Add(new JsonObject
                {
                    ["type"] = "tun",
                    ["tag"] = "tun-in",
                    ["interface_name"] = "keymapper-wintun",
                    ["inet4_address"] = "172.19.0.1/30",
                    ["auto_route"] = true,
                    ["strict_route"] = true,
                    ["stack"] = string.IsNullOrEmpty(settings.TunStack) ? "mixed" : settings.TunStack
                });
            }

            root["inbounds"] = inbounds;

            // 5. Outbounds Config
            var outbounds = new JsonArray();

            var proxyOutbound = BuildOutboundForProfile(server);
            proxyOutbound["tag"] = "proxy";
            outbounds.Add(proxyOutbound);

            outbounds.Add(new JsonObject
            {
                ["type"] = "direct",
                ["tag"] = "direct"
            });

            outbounds.Add(new JsonObject
            {
                ["type"] = "block",
                ["tag"] = "block"
            });

            outbounds.Add(new JsonObject
            {
                ["type"] = "dns",
                ["tag"] = "dns-out"
            });

            root["outbounds"] = outbounds;

            // 6. Route Rules & Per-App Proxy
            var routeRules = new JsonArray();

            routeRules.Add(new JsonObject
            {
                ["port"] = new JsonArray { 53 },
                ["outbound"] = "dns-out"
            });

            routeRules.Add(new JsonObject
            {
                ["protocol"] = new JsonArray { "dns" },
                ["outbound"] = "dns-out"
            });

            if (settings.PerAppMode != "Disabled" && settings.SelectedPerAppProcesses != null && settings.SelectedPerAppProcesses.Count > 0)
            {
                var procArray = new JsonArray();
                foreach (var proc in settings.SelectedPerAppProcesses)
                {
                    procArray.Add(proc.EndsWith(".exe") ? proc : $"{proc}.exe");
                }

                if (settings.PerAppMode == "Include")
                {
                    routeRules.Add(new JsonObject
                    {
                        ["process_name"] = procArray,
                        ["outbound"] = "proxy"
                    });
                }
                else if (settings.PerAppMode == "Exclude")
                {
                    routeRules.Add(new JsonObject
                    {
                        ["process_name"] = procArray,
                        ["outbound"] = "direct"
                    });
                }
            }

            if (settings.RoutingMode == "Rule")
            {
                routeRules.Add(new JsonObject
                {
                    ["ip_is_private"] = true,
                    ["outbound"] = "direct"
                });
            }

            string finalOutbound = "proxy";
            if (settings.RoutingMode == "Direct")
            {
                finalOutbound = "direct";
            }
            else if (settings.PerAppMode == "Include")
            {
                finalOutbound = "direct";
            }

            root["route"] = new JsonObject
            {
                ["rules"] = routeRules,
                ["auto_detect_interface"] = true,
                ["final"] = finalOutbound
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            return root.ToJsonString(options);
        }

        private static JsonObject BuildOutboundForProfile(VpnServerProfile server)
        {
            var node = new JsonObject();
            var proto = server.Protocol.ToLowerInvariant();

            var fp = string.IsNullOrWhiteSpace(server.Fingerprint) ||
                     server.Fingerprint.Equals("none", StringComparison.OrdinalIgnoreCase)
                ? "chrome"
                : server.Fingerprint;

            string fallbackSni = !string.IsNullOrEmpty(server.Sni) ? server.Sni : (!string.IsNullOrEmpty(server.Host) ? server.Host : server.Address);

            switch (proto)
            {
                case "vless":
                    node["type"] = "vless";
                    node["server"] = server.Address;
                    node["server_port"] = server.Port;
                    node["uuid"] = server.Uuid;
                    if (!string.IsNullOrEmpty(server.Flow)) node["flow"] = server.Flow;

                    if (server.Security == "tls" || server.Security == "reality")
                    {
                        var tls = new JsonObject
                        {
                            ["enabled"] = true,
                            ["insecure"] = true,
                            ["server_name"] = fallbackSni,
                            ["utls"] = new JsonObject
                            {
                                ["enabled"] = true,
                                ["fingerprint"] = fp
                            }
                        };

                        if (server.Security == "reality")
                        {
                            tls["reality"] = new JsonObject
                            {
                                ["enabled"] = true,
                                ["public_key"] = server.PublicKey,
                                ["short_id"] = server.ShortId
                            };
                        }
                        node["tls"] = tls;
                    }

                    AttachTransport(node, server);
                    break;

                case "vmess":
                    node["type"] = "vmess";
                    node["server"] = server.Address;
                    node["server_port"] = server.Port;
                    node["uuid"] = server.Uuid;
                    node["security"] = "auto";

                    if (server.Security == "tls")
                    {
                        node["tls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["insecure"] = true,
                            ["server_name"] = fallbackSni,
                            ["utls"] = new JsonObject
                            {
                                ["enabled"] = true,
                                ["fingerprint"] = fp
                            }
                        };
                    }

                    AttachTransport(node, server);
                    break;

                case "trojan":
                    node["type"] = "trojan";
                    node["server"] = server.Address;
                    node["server_port"] = server.Port;
                    node["password"] = server.Password;

                    node["tls"] = new JsonObject
                    {
                        ["enabled"] = true,
                        ["insecure"] = true,
                        ["server_name"] = fallbackSni,
                        ["utls"] = new JsonObject
                        {
                            ["enabled"] = true,
                            ["fingerprint"] = fp
                        }
                    };

                    AttachTransport(node, server);
                    break;

                case "ss":
                    node["type"] = "shadowsocks";
                    node["server"] = server.Address;
                    node["server_port"] = server.Port;
                    node["method"] = (string.IsNullOrEmpty(server.HeaderType) || server.HeaderType.ToLowerInvariant() == "none") ? "aes-256-gcm" : server.HeaderType;
                    node["password"] = server.Password;
                    break;

                case "hysteria2":
                case "hy2":
                    node["type"] = "hysteria2";
                    node["server"] = server.Address;
                    node["server_port"] = server.Port;
                    node["password"] = server.Password;

                    var hy2Tls = new JsonObject
                    {
                        ["enabled"] = true,
                        ["insecure"] = true,
                        ["server_name"] = fallbackSni
                    };
                    node["tls"] = hy2Tls;
                    break;

                case "tuic":
                    node["type"] = "tuic";
                    node["server"] = server.Address;
                    node["server_port"] = server.Port;
                    node["uuid"] = server.Uuid;
                    node["password"] = server.Password;
                    node["congestion_control"] = "bbr";
                    node["zero_rtt_handshake"] = false;

                    var tuicTls = new JsonObject
                    {
                        ["enabled"] = true,
                        ["insecure"] = true,
                        ["server_name"] = fallbackSni
                    };
                    if (!string.IsNullOrEmpty(server.Alpn))
                    {
                        var alpnArray = new JsonArray();
                        foreach (var a in server.Alpn.Split(',')) alpnArray.Add(a.Trim());
                        tuicTls["alpn"] = alpnArray;
                    }
                    node["tls"] = tuicTls;
                    break;

                default:
                    node["type"] = "direct";
                    break;
            }

            return node;
        }

        private static void AttachTransport(JsonObject node, VpnServerProfile server)
        {
            var transport = server.Transport?.ToLowerInvariant();
            var usesLegacyHttpHeader = transport == "tcp" &&
                                       server.HeaderType.Equals("http", StringComparison.OrdinalIgnoreCase);

            if (usesLegacyHttpHeader || transport == "http" || transport == "h2")
            {
                var httpObj = new JsonObject
                {
                    ["type"] = "http",
                    ["path"] = string.IsNullOrEmpty(server.Path) ? "/" : server.Path
                };
                if (!string.IsNullOrEmpty(server.Host))
                {
                    httpObj["host"] = new JsonArray { server.Host };
                }
                node["transport"] = httpObj;
            }
            else if (transport == "ws")
            {
                var wsObj = new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = string.IsNullOrEmpty(server.Path) ? "/" : server.Path
                };
                if (!string.IsNullOrEmpty(server.Host))
                {
                    wsObj["headers"] = new JsonObject
                    {
                        ["Host"] = server.Host
                    };
                }
                node["transport"] = wsObj;
            }
            else if (transport == "grpc")
            {
                node["transport"] = new JsonObject
                {
                    ["type"] = "grpc",
                    ["service_name"] = server.Path
                };
            }
            else if (transport == "httpupgrade")
            {
                var huObj = new JsonObject
                {
                    ["type"] = "httpupgrade",
                    ["path"] = string.IsNullOrEmpty(server.Path) ? "/" : server.Path
                };
                if (!string.IsNullOrEmpty(server.Host)) huObj["host"] = server.Host;
                node["transport"] = huObj;
            }
        }
    }
}
