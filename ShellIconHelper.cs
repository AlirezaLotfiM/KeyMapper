using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace KeyMapper
{
    public static class ShellIconHelper
    {
        #region COM Interfaces for LNK Shortcut Resolution
        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLink { }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct WIN32_FIND_DATAW
        {
            public uint dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out WIN32_FIND_DATAW pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxDir);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxArgs);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPath, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszPath);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010c-0000-0000-C000-000000000046")]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            void IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
        }
        #endregion

        #region Shell API Declarations
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);
        #endregion

        private static readonly ConcurrentDictionary<string, ImageSource?> IconCache = new(StringComparer.OrdinalIgnoreCase);

        public static ImageSource? GetIconForPath(string path, bool isDirectory = false)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string resolvedPath = ResolveShortcutTarget(ResolveFullPath(path));
            bool resolvedIsDir = isDirectory || Directory.Exists(resolvedPath);
            string key = (resolvedIsDir ? "DIR:" : "FILE:") + resolvedPath;

            if (IconCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            ImageSource? img = ExtractIconOrThumbnail(resolvedPath, resolvedIsDir);
            IconCache[key] = img;
            return img;
        }

        public static string ResolveShortcutTarget(string shortcutPath)
        {
            if (string.IsNullOrWhiteSpace(shortcutPath) || !shortcutPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || !File.Exists(shortcutPath))
            {
                return shortcutPath;
            }

            try
            {
                var link = (IShellLinkW)new ShellLink();
                ((IPersistFile)link).Load(shortcutPath, 0);

                // Try explicit icon location first if shortcut specifies custom icon
                var iconPath = new StringBuilder(260);
                link.GetIconLocation(iconPath, iconPath.Capacity, out _);
                string explicitIcon = iconPath.ToString();
                if (!string.IsNullOrWhiteSpace(explicitIcon) && File.Exists(explicitIcon))
                {
                    return explicitIcon;
                }

                // Resolve target path
                var sb = new StringBuilder(260);
                link.GetPath(sb, sb.Capacity, out _, 0);
                string target = sb.ToString();
                if (!string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
                {
                    return target;
                }
            }
            catch { }

            return shortcutPath;
        }

        public static string ResolveFullPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            if (path.Equals("notepad.exe", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.SystemDirectory, "notepad.exe");
            if (path.Equals("calc.exe", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.SystemDirectory, "calc.exe");
            if (path.Equals("cmd.exe", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(Environment.SystemDirectory, "cmd.exe");

            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    return Path.GetFullPath(path);
                }
            }
            catch { }

            return path;
        }

        private static ImageSource? ExtractIconOrThumbnail(string path, bool isDirectory)
        {
            try
            {
                // 1. Image Thumbnail Preview
                if (!isDirectory && File.Exists(path))
                {
                    string ext = Path.GetExtension(path).ToLowerInvariant();
                    if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp")
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path, UriKind.Absolute);
                        bitmap.DecodePixelWidth = 160;
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        return bitmap;
                    }

                    // 2. Extract Clean Target Icon for EXEs and resolved files
                    using (var icon = Icon.ExtractAssociatedIcon(path))
                    {
                        if (icon != null)
                        {
                            var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                                icon.Handle,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            bitmapSource.Freeze();
                            return bitmapSource;
                        }
                    }
                }

                // 3. Fallback Shell API (SHGetFileInfo) without SHGFI_LINKOVERLAY for clean icon
                SHFILEINFO shfi = new SHFILEINFO();
                uint flags = SHGFI_ICON | SHGFI_LARGEICON;
                uint fileAttr = FILE_ATTRIBUTE_NORMAL;

                if (isDirectory)
                {
                    fileAttr = FILE_ATTRIBUTE_DIRECTORY;
                    flags |= SHGFI_USEFILEATTRIBUTES;
                }
                else if (!File.Exists(path))
                {
                    flags |= SHGFI_USEFILEATTRIBUTES;
                }

                IntPtr res = SHGetFileInfo(path, fileAttr, ref shfi, (uint)Marshal.SizeOf(shfi), flags);
                if (res != IntPtr.Zero && shfi.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            shfi.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bitmapSource.Freeze();
                        return bitmapSource;
                    }
                    finally
                    {
                        DestroyIcon(shfi.hIcon);
                    }
                }
            }
            catch { }

            return null;
        }
    }
}
