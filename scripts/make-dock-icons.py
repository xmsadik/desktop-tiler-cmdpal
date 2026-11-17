"""Generate the Dock band's per-desktop icons in src/DesktopTiler/Assets/Dock.

Each desktop button shows a square: filled if the desktop has app windows, outlined if it's
empty. The active desktop's square additionally gets a blue ring around it. Glyph icons can't
carry a second colour, so these are PNGs, one per state and theme:

    desktop-{occupied|empty}[-active]-{light|dark}.png

"light"/"dark" is the Windows theme the icon is shown on (dark square on a light theme and vice
versa).

When the Dock shows no desktop names (Compact size, labels off, or "Show desktop names" off),
the buttons drop their "1"/"2"/... label and carry the number inside a larger square instead,
which lets the Dock drop the gap between icon and label (narrower buttons):

    Numbered/desktop-{n}-{occupied|empty}[-active]-{light|dark}.png   n = 1..MAX_NUMBER

On a filled square the number is cut out (transparent, so the Dock's background shows through);
on an outlined square it's drawn in the foreground colour.

Drawn at 4x supersampling and downscaled. Run from anywhere:
python scripts/make-dock-icons.py  (needs: pip install pillow)
"""

from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

OUT = Path(__file__).resolve().parent.parent / "src" / "DesktopTiler" / "Assets" / "Dock"
SIZE = 64  # the Dock draws icons at ~16-20 px; 64 px stays crisp at 200-300% scaling
MAX_NUMBER = 20  # keep in sync with DesktopsPage.MaxNumberedIcon
FONT = Path("C:/Windows/Fonts/segoeuib.ttf")  # Segoe UI Bold

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


def draw_numbered(number: int, occupied: bool, active: bool, theme: str) -> Image.Image:
    s = 4
    w = SIZE * s
    im = Image.new("RGBA", (w, w), (0, 0, 0, 0))
    d = ImageDraw.Draw(im)

    # A bigger square than the plain icon, so the number stays legible at 16 px; the ring is
    # thinner and hugs the edge of the icon box.
    side = w * 0.76
    x0 = (w - side) / 2
    box = [x0, x0, x0 + side, x0 + side]
    radius = w * 0.09
    stroke = w * 0.06

    if occupied:
        d.rounded_rectangle(box, radius, fill=FOREGROUND[theme])
    else:
        d.rounded_rectangle(box, radius, outline=FOREGROUND[theme], width=round(stroke))

    # The number: as large as fits inside the square's inner area.
    text = str(number)
    inner = side - 2 * stroke - w * 0.06
    size = round(w * 0.62)
    while True:
        font = ImageFont.truetype(str(FONT), size)
        l, t, r, b = d.textbbox((0, 0), text, font=font)
        if (r - l) <= inner and (b - t) <= inner * 0.82:
            break
        size -= 2
    cx = cy = w / 2
    pos = (cx - (l + r) / 2, cy - (t + b) / 2)
    if occupied:
        # Cut the number out of the filled square.
        mask = Image.new("L", (w, w), 0)
        ImageDraw.Draw(mask).text(pos, text, font=font, fill=255)
        alpha = im.getchannel("A")
        alpha.paste(0, mask=mask)
        im.putalpha(alpha)
    else:
        d.text(pos, text, font=font, fill=FOREGROUND[theme])

    if active:
        gap, ring = w * 0.035, w * 0.07
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
    (OUT / "Numbered").mkdir(exist_ok=True)
    for occupied in (True, False):
        for active in (False, True):
            for theme in ("light", "dark"):
                name = f"desktop-{'occupied' if occupied else 'empty'}{'-active' if active else ''}-{theme}.png"
                draw(occupied, active, theme).save(OUT / name)
                print(name)
                for number in range(1, MAX_NUMBER + 1):
                    numbered = f"desktop-{number}-{name.removeprefix('desktop-')}"
                    draw_numbered(number, occupied, active, theme).save(OUT / "Numbered" / numbered)


if __name__ == "__main__":
    main()
