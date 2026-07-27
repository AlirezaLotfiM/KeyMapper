using System;
using System.Collections.Generic;

namespace KeyMapper
{
    public class ChecklistItemModel
    {
        public string Text { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
    }

    public class StickyNoteModel
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Title { get; set; } = "Quick Note";
        public string RtfContent { get; set; } = string.Empty;
        public string PlainTextContent { get; set; } = string.Empty;
        public string FlowDirection { get; set; } = "LeftToRight"; // LeftToRight or RightToLeft
        public string TextAlignment { get; set; } = "Left"; // Left, Center, Right
        public string ColorTheme { get; set; } = "Warm Yellow"; // Warm Yellow, Pastel Pink, Soft Mint, Sky Blue, Lavender, Dark Carbon, Warm Cream
        
        public double Left { get; set; } = 100;
        public double Top { get; set; } = 100;
        public double Width { get; set; } = 240;
        public double Height { get; set; } = 240;

        // Dimensions saved before collapsing — restored on expand
        public double ExpandedWidth { get; set; } = 0;
        public double ExpandedHeight { get; set; } = 0;
        
        public bool IsPinned { get; set; } = true; // true = Always On Top, false = Stick to Desktop
        public bool IsCollapsed { get; set; } = false;
        public bool IsHidden { get; set; } = false;
        public bool IsChecklistMode { get; set; } = false;
        public int ColumnCount { get; set; } = 1; // 1 or 2 columns
        public string TargetTranslateLanguage { get; set; } = "fa"; // Default target translate language
        
        public List<ChecklistItemModel> ChecklistItems { get; set; } = new List<ChecklistItemModel>();
        
        public string? AudioMemoPath { get; set; }
        public double AudioDurationSeconds { get; set; } = 0;
        public List<string> Images { get; set; } = new List<string>();

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;
    }
}
