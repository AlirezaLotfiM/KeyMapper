using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace KeyMapper
{
    public sealed record ThemeOption(
        string Id,
        string DisplayName,
        string Description,
        string PreviewColors);

    public static class ThemeManager
    {
        public static IReadOnlyList<ThemeOption> Themes { get; } =
            new[]
            {
                new ThemeOption(
                    "Warm Cream",
                    "Warm Cream",
                    "The original soft paper look.",
                    "Cream · cyan · gold"),
                new ThemeOption(
                    "Sky Paper",
                    "Sky Paper",
                    "Cool, clean, and easy to read.",
                    "White · blue · ice"),
                new ThemeOption(
                    "Soft Mint",
                    "Soft Mint",
                    "A calm green-tinted workspace.",
                    "White · mint · leaf")
            };

        public static string Normalize(string? themeName) =>
            themeName?.Trim() switch
            {
                "Sky Paper" => "Sky Paper",
                "Soft Mint" => "Soft Mint",
                _ => "Warm Cream"
            };

        public static void Apply(string? themeName)
        {
            if (Application.Current == null) return;

            ThemePalette palette = Normalize(themeName) switch
            {
                "Sky Paper" => new ThemePalette(
                    "#F4FAFF",
                    "#FFFFFF",
                    "#E7F4FD",
                    "#AFCFE2",
                    "#347EAA",
                    "#6DB7D9",
                    "#D8EFF9",
                    "#20364A",
                    "#60778C"),
                "Soft Mint" => new ThemePalette(
                    "#F4FBF6",
                    "#FFFFFF",
                    "#E1F4E8",
                    "#B4D4BF",
                    "#357A5C",
                    "#69B98E",
                    "#D7F0E0",
                    "#253B31",
                    "#667C70"),
                _ => new ThemePalette(
                    "#FFF8E7",
                    "#FFFDF7",
                    "#FFF0C9",
                    "#D6C59C",
                    "#318D99",
                    "#55CAD3",
                    "#CDEFF0",
                    "#26384F",
                    "#6B7B8F")
            };

            ResourceDictionary resources = Application.Current.Resources;
            resources["AppBackgroundBrush"] = Brush(palette.Background);
            resources["AppSurfaceBrush"] = Brush(palette.Surface);
            resources["AppSurfaceAltBrush"] = Brush(palette.SurfaceAlt);
            resources["AppBorderBrush"] = Brush(palette.Border);
            resources["AppAccentBrush"] = Brush(palette.Accent);
            resources["AppAccentFillBrush"] = Brush(palette.AccentFill);
            resources["AppAccentSoftBrush"] = Brush(palette.AccentSoft);
            resources["AppTextBrush"] = Brush(palette.Text);
            resources["AppMutedTextBrush"] = Brush(palette.MutedText);
        }

        private static SolidColorBrush Brush(string value)
        {
            var brush = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            return brush;
        }

        private sealed record ThemePalette(
            string Background,
            string Surface,
            string SurfaceAlt,
            string Border,
            string Accent,
            string AccentFill,
            string AccentSoft,
            string Text,
            string MutedText);
    }
}
