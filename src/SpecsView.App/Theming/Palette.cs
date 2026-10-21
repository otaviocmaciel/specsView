using System.Globalization;
using Microsoft.Win32;
using SpecsView.Core.Config;

namespace SpecsView.App.Theming;

/// <summary>Resolved colors for one render, derived from <see cref="ThemeConfig"/> and the Windows theme.</summary>
internal sealed record Palette(Color Foreground, Color Background, Color Accent, IReadOnlyList<(double Above, Color Color)> Thresholds)
{
    public static Palette From(ThemeConfig theme)
    {
        var foreground = theme.FollowSystemTheme
            ? (SystemUsesLightTheme() ? Color.Black : Color.White)
            : ParseColor(theme.Foreground, Color.White);

        // Alpha 0 pixels are click-through on layered windows; keep at least 1 so right-click works anywhere.
        var background = ParseColor(theme.Background, Color.Transparent);
        if (background.A == 0)
        {
            background = Color.FromArgb(1, background);
        }

        var thresholds = theme.Thresholds
            .Select(t => (t.Above, ParseColor(t.Color, foreground)))
            .ToList();

        return new Palette(foreground, background, ParseColor(theme.Accent, Color.DeepSkyBlue), thresholds);
    }

    /// <summary>The color for a percent value: highest threshold exceeded, else the base color.</summary>
    public Color ForPercent(double? value, Color baseColor)
    {
        var color = baseColor;
        if (value is { } v)
        {
            foreach (var (above, thresholdColor) in Thresholds)
            {
                if (v > above)
                {
                    color = thresholdColor;
                }
            }
        }

        return color;
    }

    /// <summary>Accepts #RGB, #RRGGBB and #AARRGGBB.</summary>
    public static Color ParseColor(string? text, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return fallback;
        }

        var hex = text.Trim().TrimStart('#');
        if (hex.Length == 3)
        {
            hex = string.Concat(hex.Select(c => new string(c, 2)));
        }

        if (hex.Length == 6)
        {
            hex = "FF" + hex;
        }

        return hex.Length == 8 && uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var argb)
            ? Color.FromArgb(unchecked((int)argb))
            : fallback;
    }

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int value && value != 0;
    }
}
