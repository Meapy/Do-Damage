"""Draws the Rust client's window icons from the website's favicon (website/favicon.svg, the Do
Damage crown on 15 by 15 pixels): each of its squares scaled up to whole pixels, so it stays sharp.

    python tools/draw_client_icon.py

Writes client-rs/app/icons/icon-32.png (the title bar's) and icon-256.png (the taskbar's), which
the client builds in (main.rs, window_icon). Needs Pillow.
"""
import os
import re

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SVG = os.path.join(ROOT, "website", "favicon.svg")
OUT = os.path.join(ROOT, "client-rs", "app", "icons")
SIZES = (32, 256)


def favicon():
    """The favicon as a 15 by 15 image: its rects in order, the first (the backdrop) with its round
    corners as the corner pixels left out."""
    svg = open(SVG, encoding="utf-8").read()
    size = int(re.search(r'viewBox="0 0 (\d+) \d+"', svg).group(1))
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    for n, attrs in enumerate(re.findall(r"<rect([^>]*)/>", svg)):
        a = dict(re.findall(r'(\w+)="([^"]*)"', attrs))
        x, y = int(a.get("x", 0)), int(a.get("y", 0))
        w, h = int(a["width"]), int(a["height"])
        c = a["fill"].lstrip("#")
        rgb = tuple(int(c[i:i + 2], 16) for i in (0, 2, 4)) + (255,)
        for px in range(x, x + w):
            for py in range(y, y + h):
                corner = n == 0 and "rx" in a and px in (x, x + w - 1) and py in (y, y + h - 1)
                if not corner:
                    img.putpixel((px, py), rgb)
    return img


def main():
    os.makedirs(OUT, exist_ok=True)
    src = favicon()
    for size in SIZES:
        scale = size // src.width
        big = src.resize((src.width * scale, src.height * scale), Image.NEAREST)
        icon = Image.new("RGBA", (size, size), (0, 0, 0, 0))
        icon.paste(big, ((size - big.width) // 2, (size - big.height) // 2))
        icon.save(os.path.join(OUT, "icon-%d.png" % size))
    print("icons:", ", ".join(str(s) for s in SIZES))


if __name__ == "__main__":
    main()
