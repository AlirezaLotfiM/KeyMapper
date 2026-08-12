using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace KeyMapper
{
    public static class ProcessScannerService
    {
        public static List<AppProcessItem> GetRunningApplications()
        {
            var results = new List<AppProcessItem>();
            var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var processes = Process.GetProcesses();
                foreach (var proc in processes)
                {
                    try
                    {
                        if (proc.Id <= 4) continue;
                        var procName = proc.ProcessName;
                        if (seenNames.Contains(procName)) continue;

                        string exePath = "";
                        try { exePath = proc.MainModule?.FileName ?? ""; } catch { }

                        if (string.IsNullOrWhiteSpace(proc.MainWindowTitle) && string.IsNullOrWhiteSpace(exePath)) continue;

                        seenNames.Add(procName);
                        var displayName = string.IsNullOrWhiteSpace(proc.MainWindowTitle) ? procName : proc.MainWindowTitle;

                        var icon = ExtractIconFromExe(exePath);

                        results.Add(new AppProcessItem
                        {
                            ProcessName = procName,
                            DisplayName = displayName,
                            ExecutablePath = exePath,
                            Icon = icon
                        });
                    }
                    catch
                    {
                        // Ignore inaccessible processes safely
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error scanning processes: {ex.Message}");
            }

            return results.OrderBy(x => x.DisplayName).ToList();
        }

        public static BitmapSource? ExtractIconFromExe(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try
            {
                using var sysIcon = Icon.ExtractAssociatedIcon(path);
                if (sysIcon == null) return null;

                BitmapSource? bitmapSource = null;
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                                sysIcon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bitmapSource?.Freeze();
                        }
                        catch { }
                    });
                }
                return bitmapSource;
            }
            catch
            {
                return null;
            }
        }
    }
}
