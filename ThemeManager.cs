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
                    "White · mint · leaf"),
                new ThemeOption(
                    "Midnight Pixel",
                    "Midnight Pixel",
                    "Deep navy with bright arcade cyan.",
                    "Navy · cyan · cloud"),
                new ThemeOption(
                    "Graphite Gold",
                    "Graphite Gold",
                    "A neutral dark theme with warm gold.",
                    "Graphite · gold · pearl"),
                new ThemeOption(
                    "Sunset Arcade",
                    "Sunset Arcade",
                    "A playful purple and pink night palette.",
                    "Plum · pink · lavender")
            };

        public static string Normalize(string? themeName) =>
            themeName?.Trim() switch
            {
                "Sky Paper" => "Sky Paper",
                "Soft Mint" => "Soft Mint",
                "Midnight Pixel" => "Midnight Pixel",
                "Graphite Gold" => "Graphite Gold",
                "Sunset Arcade" => "Sunset Arcade",
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
                    "#FFFFFF",
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
                    "#FFFFFF",
                    "#B4D4BF",
                    "#357A5C",
                    "#69B98E",
                    "#D7F0E0",
                    "#253B31",
                    "#667C70"),
                "Midnight Pixel" => new ThemePalette(
                    "#111827",
                    "#182235",
                    "#22304A",
                    "#0F172A",
                    "#40516F",
                    "#55D9E6",
                    "#2AA9B8",
                    "#243E55",
                    "#F4F7FB",
                    "#A8B6C8"),
                "Graphite Gold" => new ThemePalette(
                    "#181818",
                    "#222222",
                    "#2D2D2D",
                    "#151515",
                    "#494949",
                    "#F2B84B",
                    "#D99A2B",
                    "#3A3326",
                    "#F5F5F5",
                    "#B9B9B9"),
                "Sunset Arcade" => new ThemePalette(
                    "#24172D",
                    "#30203C",
                    "#442850",
                    "#1C1224",
                    "#67466F",
                    "#FF78B5",
                    "#D95394",
                    "#51324C",
                    "#FFF3FA",
                    "#D0B4CA"),
                _ => new ThemePalette(
                    "#FFF8E7",
                    "#FFFDF7",
                    "#FFF0C9",
                    "#FFFFFF",
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
            resources["AppInputBrush"] = Brush(palette.Input);
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
            string Input,
            string Border,
            string Accent,
            string AccentFill,
            string AccentSoft,
            string Text,
            string MutedText);
    }
}
