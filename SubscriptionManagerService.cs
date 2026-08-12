using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;

namespace KeyMapper
{
    public static class SubscriptionManagerService
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        static SubscriptionManagerService()
        {
            try
            {
                HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("v2rayN/6.30 KeyMapperClient/1.0");
                HttpClient.Timeout = TimeSpan.FromSeconds(15);
            }
            catch { }
        }

        public static async Task<(List<VpnServerProfile> nodes, VpnSubscription subInfo)> FetchSubscriptionAsync(VpnSubscription subscription)
        {
            var nodes = new List<VpnServerProfile>();
            if (string.IsNullOrWhiteSpace(subscription.Url)) return (nodes, subscription);

            try
            {
                var response = await HttpClient.GetAsync(subscription.Url);
                response.EnsureSuccessStatusCode();

                if (response.Headers.TryGetValues("subscription-userinfo", out var headerValues))
                {
                    foreach (var header in headerValues)
                    {
                        ParseUserInfoHeader(header, subscription);
                    }
                }

                var content = await response.Content.ReadAsStringAsync();
                nodes = LinkParserService.ParseContent(content, subscription.Name);

                subscription.NodeCount = nodes.Count;
                subscription.LastUpdated = DateTime.Now;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to fetch subscription {subscription.Url}: {ex.Message}");
            }

            return (nodes, subscription);
        }

        private static void ParseUserInfoHeader(string header, VpnSubscription sub)
        {
            var pairs = header.Split(';');
            foreach (var pair in pairs)
            {
                var kv = pair.Trim().Split('=');
                if (kv.Length == 2)
                {
                    var key = kv[0].Trim().ToLowerInvariant();
                    if (long.TryParse(kv[1].Trim(), out long val))
                    {
                        if (key == "upload") sub.UploadBytes = val;
                        else if (key == "download") sub.DownloadBytes = val;
                        else if (key == "total") sub.TotalBytes = val;
                        else if (key == "expire") sub.ExpireTimestamp = val;
                    }
                }
            }
        }
    }
}
