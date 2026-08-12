using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KeyMapper
{
    public enum FenceType
    {
        CustomShortcuts,
        FolderPortal,
        QuickActions
    }

    public class DesktopFenceItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string IconPath { get; set; } = string.Empty;
        public bool IsDirectory { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.Now;
    }

    public class DesktopFenceConfig
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Title { get; set; } = "New Fence";
        public FenceType Type { get; set; } = FenceType.CustomShortcuts;
        public string FolderPortalPath { get; set; } = string.Empty;

        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public double Width { get; set; } = 280;
        public double Height { get; set; } = 320;

        public bool IsCollapsed { get; set; }
        public bool IsPinnedToDesktop { get; set; } = true;
        public string ColorTheme { get; set; } = "Lavender"; // Warm Yellow, Pastel Pink, Soft Mint, Sky Blue, Lavender, Dark Carbon, Warm Cream
        public double FenceOpacity { get; set; } = 0.88; // 0.20 to 1.0

        public List<DesktopFenceItem> Items { get; set; } = new();
    }

    public class DesktopFencesContainer
    {
        public bool AreFencesVisible { get; set; } = true;
        public List<DesktopFenceConfig> Fences { get; set; } = new();
    }
}
