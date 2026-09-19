"""Rebuild Assets/banner.svg, the banner at the top of the README.

Run: python scripts/make-banner.py   (requires fonttools)

The wordmark is emitted as real glyph outlines rather than a <text> element with a
font-family. GitHub renders README SVGs inside an <img>, which cannot load a font
and falls back to whatever the viewer happens to have — so a text element would
render in Playfair on this machine and in something else on everyone else's.
Outlines look the same everywhere and need nothing installed.
"""
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen

ROOT = Path(__file__).resolve().parents[1]
FONT = ROOT / "Assets/Fonts/PlayfairDisplay.ttf"
OUT = ROOT / "Assets/banner.svg"

W, H = 1200, 300

# Editorial Lemon, the palette osXos ships with by default.
FOREST_TOP = "#24422F"
FOREST_BOTTOM = "#122A20"
AMBER = "#E8890B"
AMBER_DEEP = "#B45309"
INK = "#122A20"
MUTED = "#8FA89B"

WORDMARK = "osXos"
WORDMARK_PX = 104          # cap height target, in banner units
MARK_SIZE = 92
MARK_X, MARK_Y = 92, (H - 92) // 2

# The same twelve points as the app icon and IconBrand, on a 24 grid.
X24 = [(8.4, 6.6), (12, 10.2), (15.6, 6.6), (17.4, 8.4), (13.8, 12), (17.4, 15.6),
       (15.6, 17.4), (12, 13.8), (8.4, 17.4), (6.6, 15.6), (10.2, 12), (6.6, 8.4)]


def wordmark_path(text, size):
    """The text as one SVG path, plus its advance width, scaled to `size` px em."""
    font = TTFont(FONT)
    upem = font["head"].unitsPerEm
    glyphs = font.getGlyphSet()
    cmap = font.getBestCmap()
    hmtx = font["hmtx"]
    scale = size / upem

    parts, x = [], 0.0
    for ch in text:
        name = cmap.get(ord(ch))
        if name is None:
            continue
        pen = SVGPathPen(glyphs)
        glyphs[name].draw(pen)
        d = pen.getCommands()
        if d:
            # Flip Y: font space grows upward, SVG space downward.
            parts.append(f'<path transform="translate({x:.2f} 0) scale({scale:.5f} {-scale:.5f})" d="{d}"/>')
        x += hmtx[name][0] * scale

    return "".join(parts), x


def tracked(text, size, tracking):
    """Same as wordmark_path but with letter-spacing, for the small caps line."""
    font = TTFont(FONT)
    upem = font["head"].unitsPerEm
    glyphs, cmap, hmtx = font.getGlyphSet(), font.getBestCmap(), font["hmtx"]
    scale = size / upem

    parts, x = [], 0.0
    for ch in text:
        name = cmap.get(ord(ch))
        if name is None:
            x += tracking
            continue
        pen = SVGPathPen(glyphs)
        glyphs[name].draw(pen)
        d = pen.getCommands()
        if d:
            parts.append(f'<path transform="translate({x:.2f} 0) scale({scale:.5f} {-scale:.5f})" d="{d}"/>')
        x += hmtx[name][0] * scale + tracking
    return "".join(parts), x


def main():
    word, word_w = wordmark_path(WORDMARK, WORDMARK_PX)
    tag, _ = tracked("SYSTEM UTILITIES THAT EXPLAIN THEMSELVES", 21, 2.2)
    oses, _ = tracked("WINDOWS  ·  MACOS  ·  LINUX", 15, 3.0)

    text_x = MARK_X + MARK_SIZE + 44
    x_pts = " ".join(
        f"{MARK_X + px * MARK_SIZE / 24:.2f},{MARK_Y + py * MARK_SIZE / 24:.2f}" for px, py in X24)
    echo_pts = " ".join(f"{px:.2f},{py:.2f}" for px, py in X24)

    # Baselines, measured down from the top rather than centred on a box: the
    # descender of the wordmark has to clear the tagline, and eyeballing it was
    # what made the first PNG banner collide with itself.
    word_baseline = 154
    tag_baseline = 196
    os_baseline = 228

    svg = f'''<svg xmlns="http://www.w3.org/2000/svg" width="{W}" height="{H}" viewBox="0 0 {W} {H}" role="img" aria-label="osXos — system utilities that explain themselves">
  <title>osXos</title>
  <defs>
    <linearGradient id="bg" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{FOREST_TOP}"/>
      <stop offset="1" stop-color="{FOREST_BOTTOM}"/>
    </linearGradient>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="{AMBER}"/>
      <stop offset="1" stop-color="{AMBER_DEEP}"/>
    </linearGradient>
    <linearGradient id="rule" x1="0" y1="0" x2="1" y2="0">
      <stop offset="0" stop-color="{AMBER}" stop-opacity="0.9"/>
      <stop offset="1" stop-color="{AMBER}" stop-opacity="0"/>
    </linearGradient>
    <radialGradient id="glow">
      <stop offset="0" stop-color="{AMBER}" stop-opacity="0.16"/>
      <stop offset="1" stop-color="{AMBER}" stop-opacity="0"/>
    </radialGradient>
  </defs>

  <rect width="{W}" height="{H}" fill="url(#bg)"/>

  <!-- A very faint outline of the mark, bleeding off the right edge. Drawn as a
       stroke rather than a filled rectangle: a low-opacity fill has a hard edge
       wherever it stops, which read as a seam down the banner. -->
  <g opacity="0.055" fill="none" stroke="#FFFFFF" stroke-width="0.55"
     transform="translate({W - 150} {H // 2}) rotate(-12) scale(13) translate(-12 -12)">
    <rect x="2" y="2" width="20" height="20" rx="4.5"/>
    <polygon points="{echo_pts}"/>
  </g>

  <!-- A soft glow under the tile, so the mark sits in the field rather than on it. -->
  <ellipse cx="{MARK_X + MARK_SIZE / 2}" cy="{MARK_Y + MARK_SIZE / 2}"
           rx="{MARK_SIZE * 1.5}" ry="{MARK_SIZE * 1.1}" fill="url(#glow)"/>

  <!-- The mark, identical to Assets/icon.svg. -->
  <rect x="{MARK_X}" y="{MARK_Y}" width="{MARK_SIZE}" height="{MARK_SIZE}" rx="21" fill="url(#tile)"/>
  <polygon points="{x_pts}" fill="{INK}"/>

  <!-- Wordmark, as outlines: see the note at the top of scripts/make-banner.py. -->
  <g transform="translate({text_x} {word_baseline})" fill="#FFFFFF">{word}</g>

  <rect x="{text_x + 2}" y="{word_baseline + 16}" width="{word_w - 4:.0f}" height="2" fill="url(#rule)"/>

  <g transform="translate({text_x + 2} {tag_baseline})" fill="{MUTED}">{tag}</g>
  <g transform="translate({text_x + 2} {os_baseline})" fill="{AMBER}">{oses}</g>
</svg>
'''
    OUT.write_text(svg, encoding="utf-8")
    print(f"wrote {OUT} ({len(svg)} bytes)")


if __name__ == "__main__":
    main()
