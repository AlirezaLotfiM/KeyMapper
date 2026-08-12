using System;
using System.Runtime.InteropServices;

namespace KeyMapper
{
    public static class SysProxyService
    {
        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        public static void SetSystemProxy(string server, int httpPort, int socksPort = 2081)
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 1);
                    key.SetValue("ProxyServer", $"http={server}:{httpPort};https={server}:{httpPort};socks={server}:{socksPort}");
                    key.SetValue("ProxyOverride", "localhost;127.*;10.*;172.16.*;172.17.*;172.18.*;172.19.*;172.20.*;172.21.*;172.22.*;172.23.*;172.24.*;172.25.*;172.26.*;172.27.*;172.28.*;172.29.*;172.30.*;172.31.*;192.168.*;<local>");
                }
                RefreshWinInet();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to set system proxy: {ex.Message}");
            }
        }

        public static void ClearSystemProxy()
        {
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true);
                if (key != null)
                {
                    key.SetValue("ProxyEnable", 0);
                }
                RefreshWinInet();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear system proxy: {ex.Message}");
            }
        }

        private static void RefreshWinInet()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }
    }
}
