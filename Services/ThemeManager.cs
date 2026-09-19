using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using OsXos.Models;

namespace OsXos.Services;

public static class ThemeManager
{
    public static void ApplyTheme(ThemeDefinition theme)
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;

        // Switch the variant before the tokens so Fluent's built-in templates —
        // combo popups, text boxes, scrollbars, menus, tooltips — follow the palette
        // instead of staying light and rendering unreadable on a dark canvas.
        Application.Current.RequestedThemeVariant =
            theme.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        SetBrush(res, "Bg0", theme.Bg0);
        SetBrush(res, "BgSidebar", theme.BgSidebar);
        SetBrush(res, "BgTopBar", theme.BgTopBar);
        SetBrush(res, "BgElevated", theme.BgElevated);
        SetBrush(res, "Surface", theme.Surface);
        SetBrush(res, "SurfaceHover", theme.SurfaceHover);
        SetBrush(res, "SurfaceActive", theme.SurfaceActive);

        SetBrush(res, "Line", theme.Line);
        SetBrush(res, "LineBright", theme.LineBright);
        SetBrush(res, "LineSubtle", theme.LineSubtle);

        SetBrush(res, "Text", theme.Text);
        SetBrush(res, "Muted", theme.Muted);
        SetBrush(res, "Dim", theme.Dim);

        SetBrush(res, "Accent", theme.Accent);
        SetBrush(res, "AccentHover", theme.AccentHover);
        SetBrush(res, "AccentActive", theme.Accent);
        SetBrush(res, "OnAccent", theme.OnAccent);
        SetAlphaBrush(res, "AccentMuted", theme.Accent, 0.15);

        // Highlight/Accent color (Cyan / Theme Accent)
        SetBrush(res, "Cyan", theme.Cyan);
        SetBrush(res, "SidebarAccent", theme.SidebarAccent ?? theme.Cyan);
        SetAlphaBrush(res, "CyanHover", theme.Cyan, 0.85);
        SetAlphaBrush(res, "CyanMuted", theme.Cyan, 0.15);

        SetBrush(res, "Gold", theme.Gold);
        SetAlphaBrush(res, "GoldMuted", theme.Gold, 0.15);

        SetBrush(res, "Green", theme.Green);
        SetAlphaBrush(res, "GreenMuted", theme.Green, 0.15);

        SetBrush(res, "Red", theme.Red);
        SetAlphaBrush(res, "RedMuted", theme.Red, 0.15);

        // Sidebar dynamic states. On a dark palette the sidebar is already near-black,
        // so the same white overlays read as too faint — they get a little more lift.
        SetAlphaBrush(res, "SidebarHover", "#FFFFFF", theme.IsDark ? 0.10 : 0.08);
        SetAlphaBrush(res, "SidebarSelected", "#FFFFFF", theme.IsDark ? 0.18 : 0.15);
        // A black inner edge disappears against a near-black sidebar, so dark palettes
        // get a faint light edge instead.
        if (theme.IsDark) SetAlphaBrush(res, "SidebarBorder", "#FFFFFF", 0.08);
        else SetAlphaBrush(res, "SidebarBorder", "#000000", 0.25);
        SetAlphaBrush(res, "SidebarDivider", "#FFFFFF", theme.IsDark ? 0.12 : 0.10);

        // Fluent resolves input text, placeholders and backgrounds from its own
        // variant-scoped brushes, not from the setters above — so on a variant switch
        // a text box could end up with light-variant ink on a light palette, or the
        // reverse, and the typed text disappeared. Pinning these to the palette makes
        // the theme authoritative and removes that whole class of bug.
        foreach (var key in new[]
                 {
                     "TextControlForeground", "TextControlForegroundPointerOver",
                     "TextControlForegroundFocused", "TextControlForegroundDisabled",
                     "ComboBoxForeground", "ComboBoxForegroundPointerOver",
                     "ComboBoxForegroundPressed", "ComboBoxForegroundFocused",
                     "ComboBoxDropDownForeground", "ComboBoxItemForeground",
                 })
            SetBrush(res, key, theme.Text);

        // Placeholders take Muted, not Dim. Dim is tuned for decorative captions and
        // lands around 2.8:1 on a light canvas — legible enough for a hint you already
        // know, not for one you are reading for the first time.
        foreach (var key in new[]
                 {
                     "TextControlPlaceholderForeground", "TextControlPlaceholderForegroundPointerOver",
                     "TextControlPlaceholderForegroundFocused", "ComboBoxPlaceHolderForeground",
                 })
            SetBrush(res, key, theme.Muted);

        foreach (var key in new[]
                 {
                     "TextControlBackground", "TextControlBackgroundPointerOver",
                     "TextControlBackgroundFocused", "ComboBoxBackground",
                     "ComboBoxBackgroundPointerOver", "ComboBoxBackgroundPressed",
                 })
            SetBrush(res, key, theme.Surface);

        SetBrush(res, "ComboBoxDropDownBackground", theme.BgElevated);
        SetBrush(res, "TextControlBorderBrush", theme.Line);
        SetBrush(res, "TextControlBorderBrushPointerOver", theme.LineBright);
        SetBrush(res, "TextControlBorderBrushFocused", theme.Cyan);
        SetBrush(res, "ComboBoxBorderBrush", theme.Line);
        SetBrush(res, "ComboBoxBorderBrushPointerOver", theme.LineBright);

        // Fluent Theme system accent overrides (for ComboBox, focus rings, etc.)
        try
        {
            var cyanColor = Color.Parse(theme.Cyan);
            res["SystemAccentColor"] = cyanColor;
            res["SystemControlHighlightAccentBrush"] = new SolidColorBrush(cyanColor);
            res["SystemControlHighlightListLowBrush"] = new SolidColorBrush(Color.FromArgb(20, cyanColor.R, cyanColor.G, cyanColor.B));
            res["SystemControlHighlightListMediumBrush"] = new SolidColorBrush(Color.FromArgb(40, cyanColor.R, cyanColor.G, cyanColor.B));
            res["SystemControlHighlightListAccentLowBrush"] = new SolidColorBrush(Color.FromArgb(30, cyanColor.R, cyanColor.G, cyanColor.B));
            res["SystemControlHighlightListAccentMediumBrush"] = new SolidColorBrush(Color.FromArgb(60, cyanColor.R, cyanColor.G, cyanColor.B));
            res["SystemControlHighlightListAccentHighBrush"] = new SolidColorBrush(Color.FromArgb(90, cyanColor.R, cyanColor.G, cyanColor.B));
        }
        catch { }
    }

    public static void ApplyFont(FontDefinition font)
    {
        if (Application.Current == null) return;
        var res = Application.Current.Resources;
        res["AppFontFamily"] = new FontFamily(font.FontFamilyString);
    }

    static void SetBrush(IResourceDictionary res, string key, string hex)
    {
        try
        {
            var color = Color.Parse(hex);
            res[key] = new SolidColorBrush(color);
        }
        catch
        {
            // ignore malformed color
        }
    }

    static void SetAlphaBrush(IResourceDictionary res, string key, string hex, double alpha)
    {
        try
        {
            var color = Color.Parse(hex);
            var a = (byte)(Math.Clamp(alpha, 0.0, 1.0) * 255);
            res[key] = new SolidColorBrush(Color.FromArgb(a, color.R, color.G, color.B));
        }
        catch
        {
            // ignore
        }
    }
}
