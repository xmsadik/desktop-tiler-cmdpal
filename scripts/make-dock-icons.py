"""Generate the Dock band's per-desktop icons in src/DesktopTiler/Assets/Dock.

Each desktop button shows a square: filled if the desktop has app windows, outlined if it's
empty. The active desktop's square additionally gets a blue ring around it. Glyph icons can't
carry a second colour, so these are PNGs, one per state and theme:

    desktop-{occupied|empty}[-active]-{light|dark}.png

"light"/"dark" is the Windows theme the icon is shown on (dark square on a light theme and vice
versa). Drawn at 4x supersampling and downscaled. Run from anywhere:
python scripts/make-dock-icons.py  (needs: pip install pillow)
"""

from pathlib import Path

from PIL import Image, ImageDraw

OUT = Path(__file__).resolve().parent.parent / "src" / "DesktopTiler" / "Assets" / "Dock"
SIZE = 64  # the Dock draws icons at ~16-20 px; 64 px stays crisp at 200-300% scaling

FOREGROUND = {"light": (0x1A, 0x1A, 0x1A, 255), "dark": (0xFF, 0xFF, 0xFF, 255)}
# Windows' default accent blue, in the shade Fluent uses on each theme.
ACCENT = {"light": (0x00, 0x78, 0xD4, 255), "dark": (0x4C, 0xC2, 0xFF, 255)}


def draw(occupied: bool, active: bool, theme: str) -> Image.Image:
    s = 4
    w = SIZE * s
    im = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    # The square fills the same share of the icon box as the Segoe Checkbox glyph, so the
    # buttons keep their size; the ring sits in the margin around it.
    side = w * 0.56
    x0 = (w - side) / 2
    box = [x0, x0, x0 + side, x0 + side]
    radius = w * 0.07
    stroke = w * 0.065

    if occupied:
        d.rounded_rectangle(box, radius, fill=FOREGROUND[theme])
    else:
        d.rounded_rectangle(box, radius, outline=FOREGROUND[theme], width=round(stroke))

    if active:
        gap, ring = w * 0.05, w * 0.11
        outer = x0 - gap - ring
        d.rounded_rectangle(
            [outer, outer, w - outer, w - outer],
            radius + gap + ring,
            outline=ACCENT[theme],
            width=round(ring),
        )

    return im.resize((SIZE, SIZE), Image.LANCZOS)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    for occupied in (True, False):
        for active in (False, True):
            for theme in ("light", "dark"):
                name = f"desktop-{'occupied' if occupied else 'empty'}{'-active' if active else ''}-{theme}.png"
                draw(occupied, active, theme).save(OUT / name)
                print(name)


if __name__ == "__main__":
    main()
