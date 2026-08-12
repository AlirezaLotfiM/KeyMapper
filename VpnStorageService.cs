using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace KeyMapper
{
    public static class VpnStorageService
    {
        private static readonly string ServersFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpn_servers.json");
        private static readonly string SubscriptionsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpn_subscriptions.json");
        private static readonly string SettingsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "vpn_settings.json");

        public static List<VpnServerProfile>? LoadServers()
        {
            try
            {
                if (File.Exists(ServersFilePath))
                {
                    var json = File.ReadAllText(ServersFilePath);
                    return JsonSerializer.Deserialize<List<VpnServerProfile>>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load VPN servers: {ex.Message}");
            }
            return null;
        }

        public static void SaveServers(IEnumerable<VpnServerProfile> servers)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(servers, options);
                File.WriteAllText(ServersFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save VPN servers: {ex.Message}");
            }
        }

        public static List<VpnSubscription>? LoadSubscriptions()
        {
            try
            {
                if (File.Exists(SubscriptionsFilePath))
                {
                    var json = File.ReadAllText(SubscriptionsFilePath);
                    return JsonSerializer.Deserialize<List<VpnSubscription>>(json);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load VPN subscriptions: {ex.Message}");
            }
            return null;
        }

        public static void SaveSubscriptions(IEnumerable<VpnSubscription> subscriptions)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(subscriptions, options);
                File.WriteAllText(SubscriptionsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save VPN subscriptions: {ex.Message}");
            }
        }

        public static VpnSettings LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<VpnSettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load VPN settings: {ex.Message}");
            }
            return new VpnSettings();
        }

        public static void SaveSettings(VpnSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save VPN settings: {ex.Message}");
            }
        }
    }
}
