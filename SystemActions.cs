using System;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Windows;

namespace KeyMapper
{
    public static class SystemActions
    {
        private const uint SHERB_NOCONFIRMATION = 0x00000001;
        private const uint SHERB_NOPROGRESSUI = 0x00000002;
        private const uint SHERB_NOSOUND = 0x00000004;
        private const ushort VK_VOLUME_MUTE = 0xAD;

        [DllImport("user32.dll")]
        private static extern bool LockWorkStation();

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

        /// <summary>
        /// Attempts to parse and execute a system utility action.
        /// </summary>
        /// <param name="command">The system action command (e.g., mute, lock, empty, ip)</param>
        /// <param name="message">Status message to display or notify</param>
        /// <returns>True if the command matched and ran, False otherwise</returns>
        public static bool TryExecute(string command, out string message)
        {
            message = string.Empty;
            string cmdLower = command.Trim().ToLower();

            switch (cmdLower)
            {
                case "lock":
                    try
                    {
                        bool locked = LockWorkStation();
                        message = locked ? "Workstation locked." : "Failed to lock workstation.";
                        return true;
                    }
                    catch (Exception ex)
                    {
                        message = $"Lock failed: {ex.Message}";
                        return true; // Command matched, but failed
                    }

                case "empty":
                    try
                    {
                        // 0 is S_OK
                        int res = SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI | SHERB_NOSOUND);
                        message = res == 0 ? "Recycle Bin emptied silently." : "Failed to empty Recycle Bin or already empty.";
                        return true;
                    }
                    catch (Exception ex)
                    {
                        message = $"Empty Bin failed: {ex.Message}";
                        return true;
                    }

                case "mute":
                    try
                    {
                        InputSimulator.SimulateKey(VK_VOLUME_MUTE);
                        message = "System volume muted/unmuted.";
                        return true;
                    }
                    catch (Exception ex)
                    {
                        message = $"Volume toggle failed: {ex.Message}";
                        return true;
                    }

                case "ip":
                    try
                    {
                        var host = Dns.GetHostEntry(Dns.GetHostName());
                        var localIp = host.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork)?.ToString();

                        if (!string.IsNullOrEmpty(localIp))
                        {
                            Clipboard.SetText(localIp);
                            message = $"Local IP Address copied: {localIp}";
                        }
                        else
                        {
                            message = "Could not find a local IPv4 address.";
                        }
                        return true;
                    }
                    catch (Exception ex)
                    {
                        message = $"Failed to get local IP: {ex.Message}";
                        return true;
                    }

                default:
                    return false; // Not a system command
            }
        }
    }
}
