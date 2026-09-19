using Avalonia.Media;

namespace OsXos.Models;

public sealed record ThemeDefinition(
    string Key,
    string Name,
    string Description,
    string PaletteLabel,
    string Bg0,
    string BgSidebar,
    string BgTopBar,
    string BgElevated,
    string Surface,
    string SurfaceHover,
    string SurfaceActive,
    string Line,
    string LineBright,
    string LineSubtle,
    string Text,
    string Muted,
    string Dim,
    string Accent,
    string AccentHover,
    string Cyan,
    string Gold,
    string Green,
    string Red,
    // Fluent's own control templates (combo popups, text boxes, scrollbars, menus)
    // pick their colors from the app's theme variant, not from these tokens. A dark
    // palette without this flag leaves those controls rendering light-on-light.
    bool IsDark = false,
    // Ink for text sitting on Accent. White suits the deep accents the light palettes
    // use; a bright accent needs dark ink instead.
    string OnAccent = "#FFFFFF",
    // Accent for marks drawn on the sidebar — the brand cog, the selected-nav bar.
    // Normally the same highlight used elsewhere, but a palette whose sidebar IS its
    // accent would render those invisible, so it can name a contrasting one instead.
    string? SidebarAccent = null)
{
    public IBrush PrimaryBrush => new SolidColorBrush(Color.Parse(BgSidebar));
    public IBrush SecondaryBrush => new SolidColorBrush(Color.Parse(Cyan));
    public IBrush BackgroundBrush => new SolidColorBrush(Color.Parse(Bg0));
    public IBrush TextBrush => new SolidColorBrush(Color.Parse(Text));
}

public sealed record FontDefinition(
    string Key,
    string Name,
    string FontFamilyString,
    string SampleText)
{
    public FontFamily Family => new(FontFamilyString);
}

public static class ThemeCatalog
{
    public static readonly IReadOnlyList<ThemeDefinition> Themes = new List<ThemeDefinition>
    {
        new(
            Key: "default",
            Name: "Editorial Lemon",
            Description: "Warm ivory canvas with botanical deep forest green sidebar",
            PaletteLabel: "EDITORIAL",
            Bg0: "#FAF7EE",
            BgSidebar: "#183327",
            BgTopBar: "#F4EFE3",
            BgElevated: "#FFFDF9",
            Surface: "#FFFFFF",
            SurfaceHover: "#F2ECE0",
            SurfaceActive: "#E8DFC9",
            Line: "#E5DDCB",
            LineBright: "#D4C9B3",
            LineSubtle: "#F0E9DC",
            Text: "#17261F",
            Muted: "#5A6B62",
            Dim: "#8C9B92",
            Accent: "#183327",
            AccentHover: "#224535",
            Cyan: "#D97706",
            Gold: "#B45309",
            Green: "#15803D",
            Red: "#BE123C"
        ),
        new(
            Key: "tuscan-vine",
            Name: "Tuscan Vine",
            Description: "Sunrise Gold (#EDB964) with Deep Vine Green (#3B3D26)",
            PaletteLabel: "TUSCAN SUNRISE",
            Bg0: "#F8F5EC",
            BgSidebar: "#3B3D26",
            BgTopBar: "#F0EDE0",
            BgElevated: "#FFFEFB",
            Surface: "#FFFFFF",
            SurfaceHover: "#ECE7D7",
            SurfaceActive: "#E0D9C3",
            Line: "#DFDAC5",
            LineBright: "#CCC5AB",
            LineSubtle: "#EBE7D6",
            Text: "#262719",
            Muted: "#686A52",
            Dim: "#93967C",
            Accent: "#3B3D26",
            AccentHover: "#4B4E32",
            Cyan: "#EDB964",
            Gold: "#D99F45",
            Green: "#57732F",
            Red: "#C2410C"
        ),
        new(
            Key: "sunlit-forest",
            Name: "Sunlit Forest",
            Description: "Sunlit Chartreuse (#DCD870) with Deep Forest Green (#223D22)",
            PaletteLabel: "ALPINE FOREST",
            Bg0: "#F4F6ED",
            BgSidebar: "#223D22",
            BgTopBar: "#E9EFE0",
            BgElevated: "#FCFDF9",
            Surface: "#FFFFFF",
            SurfaceHover: "#E4ECD8",
            SurfaceActive: "#D5DEC6",
            Line: "#D6DFC6",
            LineBright: "#BFCBA9",
            LineSubtle: "#E8EFE0",
            Text: "#1A2C1A",
            Muted: "#546954",
            Dim: "#839983",
            Accent: "#223D22",
            AccentHover: "#2D4F2D",
            Cyan: "#A3B533",
            Gold: "#C5B834",
            Green: "#2D7D32",
            Red: "#C2410C"
        ),
        new(
            Key: "midnight-teal",
            Name: "Midnight Teal",
            Description: "Misty Blue (#83A7B4) with Midnight Teal (#062635)",
            PaletteLabel: "ARCTIC TEAL",
            Bg0: "#EFF4F7",
            BgSidebar: "#062635",
            BgTopBar: "#E3ECF0",
            BgElevated: "#F8FBFC",
            Surface: "#FFFFFF",
            SurfaceHover: "#DEE9EE",
            SurfaceActive: "#CBDCE3",
            Line: "#CDDCE2",
            LineBright: "#AFC5CE",
            LineSubtle: "#E4EEF2",
            Text: "#0B202B",
            Muted: "#4E6773",
            Dim: "#809AA6",
            Accent: "#062635",
            AccentHover: "#0B3B52",
            Cyan: "#2B7894",
            Gold: "#D97706",
            Green: "#0D9488",
            Red: "#E11D48"
        ),
        new(
            Key: "deep-harbor",
            Name: "Deep Harbor",
            Description: "Pale Mist (#C0C8CA) with Deep Harbor Slate (#2B4851)",
            PaletteLabel: "COASTAL FJORD",
            Bg0: "#F0F4F5",
            BgSidebar: "#2B4851",
            BgTopBar: "#E5ECEE",
            BgElevated: "#F9FBFC",
            Surface: "#FFFFFF",
            SurfaceHover: "#DFE8EA",
            SurfaceActive: "#CCD9DC",
            Line: "#D0DCDD",
            LineBright: "#B5C6C8",
            LineSubtle: "#E6EFF0",
            Text: "#1C2E33",
            Muted: "#53686D",
            Dim: "#83999E",
            Accent: "#2B4851",
            AccentHover: "#395E69",
            Cyan: "#487B8A",
            Gold: "#D97706",
            Green: "#059669",
            Red: "#E11D48"
        ),

        // Indigo sidebar, lavender-white canvas, and the blush used as the accent
        // itself rather than as text — it is far too light to read on white, but with
        // the indigo as its ink it makes a primary button that carries both colours.
        new(
            Key: "orchid-dusk",
            Name: "Orchid Dusk",
            Description: "Deep indigo and blush over a lavender-white page",
            PaletteLabel: "ORCHID DUSK",
            Bg0: "#F7F0F8",
            BgSidebar: "#2E294E",
            BgTopBar: "#F0E7F2",
            BgElevated: "#FFFDFF",
            Surface: "#FFFFFF",
            SurfaceHover: "#F2E8F4",
            SurfaceActive: "#E7D8EB",
            Line: "#E3D5E7",
            LineBright: "#CDB8D3",
            LineSubtle: "#F0E7F2",
            Text: "#241F3D",
            Muted: "#5E5578",
            Dim: "#8E85A6",
            Accent: "#EFBCD5",
            AccentHover: "#F7CCE0",
            // Labels and indicators need a rose dark enough to read on white, so the
            // blush is deepened rather than used raw.
            Cyan: "#B0457C",
            Gold: "#B26A12",
            Green: "#177A52",
            Red: "#C4335C",
            IsDark: false,
            OnAccent: "#2E294E"
        ),
        // Two colours and nothing else: a near-white page and one electric blue,
        // used at full strength for the whole sidebar rather than sprinkled around.
        new(
            Key: "blueprint",
            Name: "Blueprint",
            Description: "Electric blue against plain paper, and nothing else",
            PaletteLabel: "SWISS BLUEPRINT",
            Bg0: "#F8F8F8",
            BgSidebar: "#173AFF",
            BgTopBar: "#F1F1F1",
            BgElevated: "#FFFFFF",
            Surface: "#FFFFFF",
            SurfaceHover: "#EDEDED",
            SurfaceActive: "#E0E0E0",
            Line: "#E2E2E2",
            LineBright: "#C9C9C9",
            LineSubtle: "#EFEFEF",
            Text: "#121212",
            Muted: "#5A5A5A",
            Dim: "#8A8A8A",
            Accent: "#173AFF",
            AccentHover: "#0E2FD9",
            Cyan: "#173AFF",
            Gold: "#A9740B",
            Green: "#087A3F",
            Red: "#D32F2F",
            IsDark: false,
            OnAccent: "#FFFFFF",
            // The sidebar is the accent here, so the brand mark and the selected-nav
            // bar would disappear into it. They go white instead.
            SidebarAccent: "#FFFFFF"
        ),

        // ---- dark palettes ----
        //
        // A library screen is 80-odd pieces of saturated cover art. The chrome's job
        // is to be the least colourful thing on it, so both dark palettes stay
        // low-chroma and let the artwork carry the colour. Neither uses a neutral
        // grey ground: a slight temperature is what stops a dark theme reading as a
        // default. Each pairs a cool ground with a warm highlight, or the reverse,
        // so the accent never disappears into the background it sits on.
        // Harbor Night keeps the structure the light palettes rely on: a sidebar that
        // is its own saturated colour, and a canvas a clear step lighter so the
        // content card still reads as a lifted plane. The single warm lamp carries
        // every primary action against an otherwise cold room.
        new(
            Key: "harbor-night",
            Name: "Harbor Night",
            Description: "Wet slate and fog, lit by one warm harbour lamp",
            PaletteLabel: "NOCTURNE HARBOR",
            Bg0: "#121A1E",
            BgSidebar: "#0A1A20",
            BgTopBar: "#162025",
            BgElevated: "#1C282E",
            Surface: "#182328",
            SurfaceHover: "#212E35",
            SurfaceActive: "#2A3A42",
            Line: "#27363D",
            LineBright: "#3A4D57",
            LineSubtle: "#1C282E",
            Text: "#E8EFF2",
            Muted: "#9AACB4",
            Dim: "#6A7C85",
            Accent: "#E8B04B",
            AccentHover: "#F2C066",
            Cyan: "#F0B95C",
            Gold: "#C98A3A",
            Green: "#5FB98A",
            Red: "#D9636B",
            IsDark: true,
            OnAccent: "#12191D"
        ),
        // The warm counterpart: espresso and ink rather than slate, with the brass
        // pitched deeper so it sits on wood instead of glowing over water.
        new(
            Key: "lantern-oak",
            Name: "Lantern Oak",
            Description: "Oiled walnut and ink under aged brass lamplight",
            PaletteLabel: "NOCTURNE OAK",
            Bg0: "#1A1610",
            BgSidebar: "#140F08",
            BgTopBar: "#1F1A13",
            BgElevated: "#262019",
            Surface: "#221C15",
            SurfaceHover: "#2C251C",
            SurfaceActive: "#372E23",
            Line: "#332B20",
            LineBright: "#473C2C",
            LineSubtle: "#262019",
            Text: "#F2ECE0",
            Muted: "#AEA089",
            Dim: "#7E7160",
            Accent: "#C9903A",
            AccentHover: "#DCA34C",
            Cyan: "#D9A441",
            Gold: "#B8802E",
            Green: "#86A961",
            Red: "#C86A55",
            IsDark: true,
            OnAccent: "#1A1409"
        )
    };

    public static readonly IReadOnlyList<FontDefinition> Fonts = new List<FontDefinition>
    {
        new(
            Key: "bundled-inter",
            Name: "Bundled Inter (Embedded Variable Sans)",
            FontFamilyString: "avares://Avalonia.Fonts.Inter/Assets#Inter, Aptos, Segoe UI Variable Text, sans-serif",
            SampleText: "The quick brown fox jumps over the lazy dog"
        ),
        new(
            Key: "aptos-variable",
            Name: "Modern Grotesque (Aptos & Segoe UI Variable)",
            FontFamilyString: "Aptos, Segoe UI Variable Text, Segoe UI Variable Display, -apple-system, BlinkMacSystemFont, 'Segoe UI', Inter, sans-serif",
            SampleText: "The quick brown fox jumps over the lazy dog"
        ),
        new(
            Key: "bahnschrift",
            Name: "Geometric Tech (Bahnschrift / DIN 1451)",
            FontFamilyString: "Bahnschrift, 'DIN Alternate', 'DIN Next LT Pro', -apple-system, sans-serif",
            SampleText: "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG 0123456789"
        ),
        new(
            Key: "segoe-variable",
            Name: "Windows UI (Segoe UI Variable)",
            FontFamilyString: "Segoe UI Variable Text, Segoe UI Variable Display, Segoe UI, sans-serif",
            SampleText: "The quick brown fox jumps over the lazy dog"
        ),
        new(
            Key: "neo-grotesque",
            Name: "Swiss Neo-Grotesque (Inter, SF Pro, Helvetica Neue)",
            // Leads with the font bundled in the app rather than a system "Inter":
            // on a machine without it the old stack fell through to Arial, which
            // renders noticeably rougher at UI sizes.
            FontFamilyString: "avares://Avalonia.Fonts.Inter/Assets#Inter, Segoe UI Variable Text, Segoe UI, Arial, sans-serif",
            SampleText: "The quick brown fox jumps over the lazy dog"
        ),
        new(
            Key: "serif",
            Name: "Editorial Serif (Georgia, Garamond, Palatino)",
            FontFamilyString: "Georgia, 'Palatino Linotype', 'Book Antiqua', Palatino, Garamond, serif",
            SampleText: "The quick brown fox jumps over the lazy dog"
        ),
        new(
            Key: "mono",
            Name: "Monospace / Code (Cascadia Code, Consolas)",
            FontFamilyString: "Cascadia Code, Consolas, 'SF Mono', Menlo, monospace",
            SampleText: "The quick brown fox jumps over the lazy dog"
        )
    };

    public static ThemeDefinition FindTheme(string? key) =>
        Themes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public static FontDefinition FindFont(string? key) =>
        Fonts.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Fonts[0];
}
