"""Regenerate every image in src/DesktopTiler/Assets from one vector-ish drawing.

The icon is three overlapping windows cascading down-right, drawn with Pillow at 4x
supersampling and downscaled, so every size stays crisp. Both the scale-100 base names (which Microsoft Store validation expects) and scale-200 variants are
written. Run from anywhere: python scripts/make-icons.py  (needs: pip install pillow)
"""

from pathlib import Path

from PIL import Image, ImageDraw

ASSETS = Path(__file__).resolve().parent.parent / "src" / "DesktopTiler" / "Assets"

LIGHT = (0x4C, 0xC2, 0xFF, 255)
MID = (0x00, 0x78, 0xD4, 255)
DARK = (0x00, 0x5A, 0x9E, 255)


def shade(color: tuple, factor: float) -> tuple:
    return tuple(int(v * factor) for v in color[:3]) + (255,)


def glyph(size: int) -> Image.Image:
    """The bare icon: three overlapping windows cascading down-right, each with a title bar,
    front one darkest; square, transparent background."""
    s = 4
    w = size * s
    im = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)
    margin, side = w * 0.05, w * 0.62
    step = (w - 2 * margin - side) / 2
    radius, bar, ring = w * 0.07, w * 0.12, w * 0.035
    for i, body in enumerate((LIGHT, MID, DARK)):
        x = y = margin + step * i
        if i:
            # Punch a transparent ring around each front window so overlapping windows read as
            # separate shapes at small sizes.
            d.rounded_rectangle([x - ring, y - ring, x + side + ring, y + side + ring], radius + ring, fill=(0, 0, 0, 0))
        d.rounded_rectangle([x, y, x + side, y + side], radius, fill=body)
        d.rounded_rectangle([x, y, x + side, y + bar], radius, fill=shade(body, 0.72), corners=(True, True, False, False))
    return im.resize((size, size), Image.LANCZOS)


def plated(width: int, height: int, fraction: float) -> Image.Image:
    """The icon centered on a transparent canvas, its side = fraction of the shorter edge."""
    canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    side = round(min(width, height) * fraction)
    canvas.alpha_composite(glyph(side), ((width - side) // 2, (height - side) // 2))
    return canvas


# name -> (width, height, icon fraction) at scale-100
TILES = {
    "Square44x44Logo": (44, 44, 1.0),
    "SmallTile": (71, 71, 0.66),
    "Square150x150Logo": (150, 150, 0.66),
    "LargeTile": (310, 310, 0.66),
    "Wide310x150Logo": (310, 150, 0.66),
    "SplashScreen": (620, 300, 0.5),
    "LockScreenLogo": (24, 24, 1.0),
}


def main() -> None:
    for old in ASSETS.glob("*.png"):
        old.unlink()

    for name, (w, h, frac) in TILES.items():
        plated(w, h, frac).save(ASSETS / f"{name}.png")
        plated(w * 2, h * 2, frac).save(ASSETS / f"{name}.scale-200.png")

    glyph(50).save(ASSETS / "StoreLogo.png")
    glyph(100).save(ASSETS / "StoreLogo.scale-200.png")

    # Taskbar / Start list / Command Palette use the unplated target sizes.
    for size in (16, 24, 32, 48, 256):
        glyph(size).save(ASSETS / f"Square44x44Logo.targetsize-{size}_altform-unplated.png")

    for f in sorted(ASSETS.glob("*.png")):
        print(f.name, Image.open(f).size)


if __name__ == "__main__":
    main()
