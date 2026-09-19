"""Rebuild osXos's SVG, PNG and multi-resolution ICO assets.

Run: python scripts/make-icon.py   (requires Pillow)

The mark is the "os" tile with the X of osXos cut out of it. It is deliberately
blunt: the smallest size in the ICO is 16px, where anything finer than a bar a
couple of pixels wide turns to mush. Colours are the Editorial Lemon palette the
app ships with by default, so the icon and the default theme agree.

The geometry below is the source of truth. App.axaml carries the same shape as a
monochrome StreamGeometry (IconBrand) for in-app use; keep the two in step.
"""
from pathlib import Path
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[1]
ASSETS = ROOT / "Assets"

# Canvas is 256 units; every coordinate below is on that grid.
CANVAS = 256
SS = 8                      # supersample factor, downsampled with LANCZOS
TILE = (16, 16, 240, 240)   # rounded square bounds
RADIUS = 52

# Deep forest tile with the amber X, rather than the other way round: a dark mark
# on an orange tile reads as an error badge, which is the last thing a maintenance
# tool should look like.
TILE_TOP = "#204234"
TILE_BOTTOM = "#122A20"
X_COLOR = "#E8890B"

# The X, as the same twelve points the StreamGeometry uses, scaled from the 24 grid.
X_POINTS_24 = [
    (8.4, 6.6), (12, 10.2), (15.6, 6.6), (17.4, 8.4), (13.8, 12), (17.4, 15.6),
    (15.6, 17.4), (12, 13.8), (8.4, 17.4), (6.6, 15.6), (10.2, 12), (6.6, 8.4),
]
X_POINTS = [(x * CANVAS / 24, y * CANVAS / 24) for x, y in X_POINTS_24]

SIZES = (16, 24, 32, 48, 64, 128, 256)
PNG_SIZES = (16, 32, 64, 128, 256, 512, 1024)


def rgb(value):
    return tuple(int(value[i:i + 2], 16) for i in (1, 3, 5))


def render(size):
    """The mark at `size` px, rendered at SS and downsampled."""
    n = size * SS
    scale = n / CANVAS

    # The tile, drawn as a mask so the gradient can be pasted through it.
    mask = Image.new("L", (n, n), 0)
    ImageDraw.Draw(mask).rounded_rectangle(
        [c * scale for c in TILE], radius=RADIUS * scale, fill=255)

    top, bottom = rgb(TILE_TOP), rgb(TILE_BOTTOM)
    gradient = Image.new("RGB", (1, n))
    for y in range(n):
        t = y / max(n - 1, 1)
        gradient.putpixel((0, y), tuple(
            round(top[i] + (bottom[i] - top[i]) * t) for i in range(3)))
    gradient = gradient.resize((n, n))

    img = Image.new("RGBA", (n, n), (0, 0, 0, 0))
    img.paste(gradient, (0, 0), mask)

    # The X is painted on rather than cut out: a hole would vanish against a dark
    # taskbar, where this still reads.
    ImageDraw.Draw(img).polygon(
        [(x * scale, y * scale) for x, y in X_POINTS], fill=rgb(X_COLOR))

    return img.resize((size, size), Image.LANCZOS)


def export_svg():
    pts = " ".join(f"{x:.2f},{y:.2f}" for x, y in X_POINTS)
    x0, y0, x1, y1 = TILE
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 {CANVAS} {CANVAS}">
<title>osXos application icon</title>
<defs><linearGradient id="tile" x1="0" y1="0" x2="0" y2="1">
<stop stop-color="{TILE_TOP}"/><stop offset="1" stop-color="{TILE_BOTTOM}"/>
</linearGradient></defs>
<rect x="{x0}" y="{y0}" width="{x1 - x0}" height="{y1 - y0}" rx="{RADIUS}" fill="url(#tile)"/>
<polygon points="{pts}" fill="{X_COLOR}"/>
</svg>
'''


def main():
    ASSETS.mkdir(parents=True, exist_ok=True)

    (ASSETS / "icon.svg").write_text(export_svg(), encoding="utf-8")

    for size in PNG_SIZES:
        render(size).save(ASSETS / f"icon-{size}.png")

    # icon.png is what Avalonia loads for the window and what the README shows.
    render(1024).save(ASSETS / "icon.png")

    # Pillow writes a genuine multi-resolution ICO when handed explicit sizes;
    # Windows picks 16 for the taskbar and 256 for the shell.
    render(256).save(ASSETS / "icon.ico", sizes=[(s, s) for s in SIZES])

    print(f"wrote {len(PNG_SIZES) + 3} assets to {ASSETS}")


if __name__ == "__main__":
    main()
