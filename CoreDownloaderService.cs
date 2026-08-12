using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace KeyMapper
{
    public static class CoreDownloaderService
    {
        private static readonly HttpClient HttpClient = new HttpClient();

        public static bool IsCoreAvailable()
        {
            var exePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sing-box.exe");
            return File.Exists(exePath);
        }

        public static async Task<bool> DownloadSingBoxCoreAsync(Action<string>? progressLogger = null)
        {
            try
            {
                var coreExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "sing-box.exe");
                if (File.Exists(coreExePath)) return true;

                progressLogger?.Invoke("[VPN CORE] Downloading latest official Sing-Box engine for Windows (x64)...");

                var downloadUrl = "https://github.com/SagerNet/sing-box/releases/download/v1.10.1/sing-box-1.10.1-windows-amd64.zip";
                var zipPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "singbox.zip");

                using (var response = await HttpClient.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    response.EnsureSuccessStatusCode();
                    using var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None);
                    await response.Content.CopyToAsync(fs);
                }

                progressLogger?.Invoke("[VPN CORE] Extracting Sing-Box engine binary...");

                var extractDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "temp_singbox");
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);

                ZipFile.ExtractToDirectory(zipPath, extractDir);

                var foundFiles = Directory.GetFiles(extractDir, "sing-box.exe", SearchOption.AllDirectories);
                if (foundFiles.Length > 0)
                {
                    File.Copy(foundFiles[0], coreExePath, true);
                    progressLogger?.Invoke("[VPN CORE] Sing-Box engine binary installed successfully!");
                }

                if (File.Exists(zipPath)) File.Delete(zipPath);
                if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);

                return File.Exists(coreExePath);
            }
            catch (Exception ex)
            {
                progressLogger?.Invoke($"[VPN ERROR] Core download failed: {ex.Message}");
                return false;
            }
        }
    }
}
