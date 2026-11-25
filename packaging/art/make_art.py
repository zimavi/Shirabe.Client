"""Draws the app icon and the installer artwork, and writes every file the app and the packages use.

The outputs are committed; run this only after changing the art:  python packaging/art/make_art.py
Needs Pillow, and the Segoe UI fonts of Windows for the pictures with text.
"""
import math
import re
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parents[2]
PACKAGING = ROOT / "packaging"

FACE = ((170, 11, 35), (254, 87, 119))  # gradient, top left to bottom right
RING = ((255, 45, 100), (255, 150, 205))
RING_RADIUS, RING_WIDTH = 403, 16
INK = (254, 251, 255)
DARK = (19, 19, 22)  # the app's window background
ACCENT = (232, 69, 107)  # AccentColor in Styles/Theme.axaml
# Behind the file names in the .dmg: Finder writes them in black or white, and both are readable on this (4.5:1).
LABEL_PLATE = (201, 74, 104)

# The katakana "shi", traced from the first 128 px icon. Absolute M, C and Z commands only, in a grid that
# STROKES_TRANSFORM places on the 1024 px canvas.
STROKES = (
    "M 288 94 C 308 93 325 96 340 103 C 405 130 470 168 517 205 C 548 228 565 248 565 280 "
    "C 565 322 530 359 485 359 C 462 359 448 354 432 340 C 380 295 320 240 260 192 C 238 174 228 160 228 140 "
    "C 228 114 254 94 288 94 Z",
    "M 142 368 C 160 368 175 370 195 376 C 255 393 330 427 380 456 C 415 478 434 505 434 540 "
    "C 434 585 398 622 355 622 C 335 622 320 618 302 605 C 245 560 185 520 120 482 C 95 466 80 445 80 420 "
    "C 80 390 108 368 142 368 Z",
    "M 1008 330 C 1045 330 1072 355 1072 392 C 1072 415 1062 440 1050 465 C 1010 545 955 620 880 698 "
    "C 830 750 770 805 700 855 C 610 920 500 972 385 1025 C 345 1043 312 1057 278 1057 "
    "C 232 1057 200 1015 200 968 C 200 932 222 910 258 896 C 280 887 300 881 320 875 C 420 840 500 805 575 755 "
    "C 660 700 720 645 770 603 C 830 543 890 470 932 398 C 952 360 970 330 1008 330 Z",
)
STROKES_TRANSFORM = (224, 224, 0.5)  # x offset, y offset, scale


def polygon(path, scale):
    """The outline of one stroke as points on a canvas of 1024 * scale pixels."""
    dx, dy, s = STROKES_TRANSFORM
    numbers = [float(n) for n in re.findall(r"-?\d+(?:\.\d+)?", path)]
    points = [((dx + x * s) * scale, (dy + y * s) * scale) for x, y in zip(numbers[::2], numbers[1::2])]
    assert len(points) % 3 == 1, "a path is one M followed by whole C commands"
    outline = [points[0]]
    for i in range(1, len(points), 3):
        p0, (p1, p2, p3) = outline[-1], points[i:i + 3]
        for step in range(1, 33):
            t = step / 32
            u = 1 - t
            outline.append(tuple(u ** 3 * a + 3 * u * u * t * b + 3 * u * t * t * c + t ** 3 * d
                                 for a, b, c, d in zip(p0, p1, p2, p3)))
    return outline


def gradient(size, start, end, first=0.0, last=1.0):
    """A diagonal gradient; first and last are where the two colors sit on the diagonal."""
    small = Image.new("RGB", (256, 256))
    small.putdata([tuple(round(a + (b - a) * min(1, max(0, ((x + y) / 510 - first) / (last - first))))
                         for a, b in zip(start, end))
                   for y in range(256) for x in range(256)])
    return small.resize((size, size), Image.BILINEAR)


def icon(size):
    """The flat icon (Windows, Linux, installer pictures) at size x size."""
    scale = 4
    canvas = 1024 * scale

    def disc(radius):
        mask = Image.new("L", (canvas, canvas))
        r = radius * scale
        ImageDraw.Draw(mask).ellipse((canvas / 2 - r, canvas / 2 - r, canvas / 2 + r - 1, canvas / 2 + r - 1), fill=255)
        return mask

    art = gradient(canvas, *FACE)
    ring = ImageChops.subtract(disc(RING_RADIUS + RING_WIDTH / 2), disc(RING_RADIUS - RING_WIDTH / 2))
    art.paste(gradient(canvas, *RING, first=0.1, last=0.9), (0, 0), ring)
    draw = ImageDraw.Draw(art)
    for path in STROKES:
        draw.polygon(polygon(path, scale), fill=INK)
    art = art.convert("RGBA")
    art.putalpha(disc(512))

    return art.resize((size, size), Image.LANCZOS)


def squircle(size, corner, exponent):
    """An anti-aliased square mask with continuous (superellipse) corners that reach `corner` pixels along each edge."""
    scale = 4
    edge, reach = size * scale, corner * scale
    curve = [(reach * (1 - math.cos(a) ** (2 / exponent)), reach * (1 - math.sin(a) ** (2 / exponent)))
             for a in (math.pi / 2 * step / 96 for step in range(97))]
    # clockwise: top left, top right, bottom right, bottom left
    points = (curve + [(edge - x, y) for x, y in reversed(curve)]
              + [(edge - x, edge - y) for x, y in curve] + [(x, edge - y) for x, y in reversed(curve)])
    mask = Image.new("L", (edge, edge))
    ImageDraw.Draw(mask).polygon(points, fill=255)
    return mask.resize((size, size), Image.LANCZOS)


def macos_icon():
    """The painted macOS icon (art/macos-source.png), cut cleanly and put on Apple's 1024 px grid with a soft shadow."""
    source = Image.open(PACKAGING / "art/macos-source.png").convert("RGB")
    assert source.size == (1024, 1024), "MAC_BODY and the corner below were measured on this picture"
    # The body without the source's jagged outline, pale fringe and painted-on shadow.
    body = source.resize((824, 824), Image.LANCZOS, box=(3, 4, 1021, 1006))
    mask = Image.new("L", (1024, 1024))
    mask.paste(squircle(824, 259, 2.52), (100, 100))

    shadow = Image.new("L", (1024, 1024))
    shadow.paste(mask.point(lambda v: round(v * 0.3)), (0, 10))
    result = Image.new("RGBA", (1024, 1024))
    result.putalpha(shadow.filter(ImageFilter.GaussianBlur(10)))
    art = Image.new("RGBA", (1024, 1024))
    art.paste(body, (100, 100))
    art.putalpha(mask)
    return Image.alpha_composite(result, art)


def svg():
    def color(rgb):
        return "#%02X%02X%02X" % rgb

    dx, dy, s = STROKES_TRANSFORM
    paths = "\n".join(f'    <path d="{p}"/>' for p in STROKES)
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="1024" height="1024" viewBox="0 0 1024 1024">
  <!-- Written by make_art.py; change the art there. -->
  <defs>
    <linearGradient id="face" gradientUnits="userSpaceOnUse" x1="0" y1="0" x2="1024" y2="1024">
      <stop offset="0" stop-color="{color(FACE[0])}"/><stop offset="1" stop-color="{color(FACE[1])}"/>
    </linearGradient>
    <linearGradient id="ring" gradientUnits="userSpaceOnUse" x1="102" y1="102" x2="922" y2="922">
      <stop offset="0" stop-color="{color(RING[0])}"/><stop offset="1" stop-color="{color(RING[1])}"/>
    </linearGradient>
  </defs>
  <circle cx="512" cy="512" r="512" fill="url(#face)"/>
  <circle cx="512" cy="512" r="{RING_RADIUS}" fill="none" stroke="url(#ring)" stroke-width="{RING_WIDTH}"/>
  <g fill="{color(INK)}" transform="translate({dx} {dy}) scale({s})">
{paths}
  </g>
</svg>
'''


def font(size, bold=False):
    return ImageFont.truetype("C:/Windows/Fonts/" + ("seguisb.ttf" if bold else "segoeui.ttf"), size)


def paste_icon(picture, size, center):
    art = icon(size)
    picture.paste(art, (center[0] - size // 2, center[1] - size // 2), art)


def centered(draw, xy, text, size, color, bold=False):
    draw.text(xy, text, font=font(size, bold), fill=color, anchor="mm")


def dmg_background(k):
    """660 x 400 points at k pixels per point. The icon positions match packaging/macos/dmg.py."""
    picture = Image.new("RGB", (660 * k, 400 * k), DARK)
    draw = ImageDraw.Draw(picture)
    centered(draw, (330 * k, 46 * k), "Shirabe", 30 * k, INK, bold=True)
    centered(draw, (330 * k, 80 * k), "Drag Shirabe onto Applications to install it", 14 * k, (160, 160, 170))
    for x in (180, 480):
        draw.rounded_rectangle(((x - 70) * k, 255 * k, (x + 70) * k, 283 * k), radius=14 * k, fill=LABEL_PLATE)
    draw.line((278 * k, 185 * k, 372 * k, 185 * k), fill=ACCENT, width=7 * k)
    draw.polygon([(366 * k, 168 * k), (392 * k, 185 * k), (366 * k, 202 * k)], fill=ACCENT)
    return picture


def splash():
    picture = Image.new("RGB", (480, 300), DARK)
    draw = ImageDraw.Draw(picture)
    paste_icon(picture, 112, (240, 105))
    centered(draw, (240, 195), "Shirabe", 32, INK, bold=True)
    centered(draw, (240, 235), "Installing…", 15, (160, 160, 170))
    return picture


def msi_dialog():
    """493 x 312. Windows writes black text from x = 180 on, so only the left strip is dark."""
    picture = Image.new("RGB", (493, 312), (255, 255, 255))
    draw = ImageDraw.Draw(picture)
    draw.rectangle((0, 0, 163, 311), fill=DARK)
    paste_icon(picture, 96, (82, 124))
    centered(draw, (82, 200), "Shirabe", 24, INK, bold=True)
    return picture


def msi_banner():
    """493 x 58. The page title is written in black on the left."""
    picture = Image.new("RGB", (493, 58), (255, 255, 255))
    paste_icon(picture, 44, (460, 29))
    ImageDraw.Draw(picture).rectangle((0, 56, 492, 57), fill=ACCENT)
    return picture


def main():
    for folder in ("art", "linux/icons", "macos", "windows"):
        (PACKAGING / folder).mkdir(parents=True, exist_ok=True)

    (PACKAGING / "art/shirabe.svg").write_text(svg(), encoding="utf-8", newline="\n")

    sizes = [256, 128, 64, 48, 40, 32, 24, 20, 16]
    frames = [icon(s) for s in sizes]
    frames[0].save(ROOT / "Shirabe.Client/Assets/app.ico", sizes=[(s, s) for s in sizes], append_images=frames[1:])
    icon(512).save(ROOT / "Shirabe.Client/Assets/Icon.png")
    for size in (48, 128, 256, 512):
        icon(size).save(PACKAGING / f"linux/icons/{size}.png")
    macos_icon().save(PACKAGING / "macos/Shirabe.icns")

    dmg_background(1).save(PACKAGING / "macos/dmg-background.png")
    dmg_background(2).save(PACKAGING / "macos/dmg-background@2x.png")
    splash().save(PACKAGING / "windows/splash.png")
    msi_dialog().save(PACKAGING / "windows/msi-dialog.bmp")
    msi_banner().save(PACKAGING / "windows/msi-banner.bmp")


if __name__ == "__main__":
    main()
