"""Regenerates Assets/ from the Drops web kit: fonts, icons and the README banner.

    python tools/make-assets.py [path-to-drops-static] [path-to-Inter-variable.ttf]

Fonts: Avalonia cannot load .woff2 and does not drive variable-font axes, so the kit's
variable fonts are pinned to the static weights the page actually uses.
Needs: pip install fonttools brotli pillow
"""
import os
import sys

from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
ASSETS = os.path.join(ROOT, "Assets")
FONTS = os.path.join(ASSETS, "Fonts")
STATIC = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "..", "fezcode.studio", "drops", "static")
INTER = sys.argv[2] if len(sys.argv) > 2 else os.path.expandvars(
    r"%LOCALAPPDATA%\Microsoft\Windows\Fonts\Inter-VariableFont_opsz,wght.ttf")

PAPER, SAGE, INK = (250, 249, 246), (63, 122, 92), (22, 19, 15)


def pin(src, out, family, style, weight, axes):
    font = instancer.instantiateVariableFont(TTFont(src), axes)
    font.flavor = None
    name = font["name"]
    for rec in list(name.names):
        if rec.nameID in (16, 17, 21, 22, 25):
            name.removeNames(nameID=rec.nameID)
    for rec in name.names:
        if rec.nameID == 1:
            rec.string = family
        elif rec.nameID == 2:
            rec.string = style
        elif rec.nameID == 4:
            rec.string = f"{family} {style}"
        elif rec.nameID == 6:
            rec.string = f"{family}-{style}".replace(" ", "")
    font["OS/2"].usWeightClass = weight
    # Bit 5 = bold, bit 6 = regular: keep them honest so weight matching works.
    sel = font["OS/2"].fsSelection & ~0b1100001
    font["OS/2"].fsSelection = sel | (0b100000 if weight >= 700 else 0b1000000)
    font["head"].macStyle = 1 if weight >= 700 else 0
    font.save(os.path.join(FONTS, out))
    print("font", out)


def fonts():
    os.makedirs(FONTS, exist_ok=True)
    for style, weight in (("Regular", 400), ("Medium", 500), ("SemiBold", 600), ("Bold", 700)):
        pin(INTER, f"Inter-{style}.ttf", "Inter", style, weight, {"wght": weight, "opsz": 16})
    bric = os.path.join(STATIC, "fonts", "bricolage-latin.woff2")
    # The browser picks the optical size from the font size: big for titles, small for the logo.
    pin(bric, "BricolageGrotesque-Bold.ttf", "Bricolage Grotesque", "Bold", 700, {"wght": 700, "opsz": 56})
    pin(bric, "BricolageGrotesqueSmall-Bold.ttf", "Bricolage Grotesque Small", "Bold", 700, {"wght": 700, "opsz": 20})


def cubic(p0, p1, p2, p3, n=64):
    pts = []
    for i in range(n + 1):
        t = i / n
        u = 1 - t
        pts.append((u ** 3 * p0[0] + 3 * u * u * t * p1[0] + 3 * u * t * t * p2[0] + t ** 3 * p3[0],
                    u ** 3 * p0[1] + 3 * u * u * t * p1[1] + 3 * u * t * t * p2[1] + t ** 3 * p3[1]))
    return pts


def droplet(size, tile=True):
    """favicon.svg, drawn large and scaled down: a sage tile with a paper droplet."""
    big = 2048
    k = big / 64
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    if tile:
        d.rounded_rectangle([4 * k, 4 * k, 60 * k, 60 * k], radius=18 * k, fill=SAGE + (255,))
    fill = PAPER + (255,) if tile else SAGE + (255,)
    left = cubic((32, 13), (32, 13), (17.5, 29), (17.5, 38.5))
    right = cubic((46.5, 38.5), (46.5, 29), (32, 13), (32, 13))
    d.polygon([(x * k, y * k) for x, y in left + right], fill=fill)
    # The bottom of the drop is the lower half of a circle of radius 14.5 centred at (32, 38.5).
    d.pieslice([17.5 * k, 24 * k, 46.5 * k, 53 * k], 0, 180, fill=fill)
    return img.resize((size, size), Image.LANCZOS)


def icons():
    master = droplet(1024)
    master.save(os.path.join(ASSETS, "icon-1024.png"))
    for s in (256, 32):
        droplet(s).save(os.path.join(ASSETS, f"icon-{s}.png"))
    master.save(os.path.join(ASSETS, "drops-icon.ico"),
                sizes=[(s, s) for s in (16, 20, 24, 32, 40, 48, 64, 128, 256)])
    print("icons")


def banner():
    w, h = 1600, 520
    img = Image.new("RGB", (w, h), PAPER)
    d = ImageDraw.Draw(img, "RGBA")
    d.rounded_rectangle([40, 40, w - 40, h - 40], radius=64, fill=(220, 235, 225))
    for r, a in ((330, 56), (470, 36)):
        d.ellipse([w - 420 - r, h // 2 - r, w - 420 + r, h // 2 + r], outline=SAGE + (a,), width=4)
    img.paste(Image.new("RGB", (w, h), PAPER), (0, 0), Image.new("L", (w, h), 0))
    # Keep the rings inside the tile.
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).rounded_rectangle([40, 40, w - 40, h - 40], radius=64, fill=255)
    out = Image.new("RGB", (w, h), PAPER)
    out.paste(img, (0, 0), mask)
    d = ImageDraw.Draw(out)
    icon = droplet(168)
    out.paste(icon, (120, 120), icon)
    title = ImageFont.truetype(os.path.join(FONTS, "BricolageGrotesque-Bold.ttf"), 150)
    body = ImageFont.truetype(os.path.join(FONTS, "Inter-Regular.ttf"), 38)
    d.text((330, 112), "Drops", font=title, fill=INK)
    d.text((124, 340), "Send a file to your other device.", font=body, fill=(58, 90, 73))
    d.text((124, 392), "It deletes itself after an hour.", font=body, fill=(58, 90, 73))
    out.save(os.path.join(ASSETS, "banner.png"))
    print("banner")


if __name__ == "__main__":
    os.makedirs(ASSETS, exist_ok=True)
    fonts()
    icons()
    banner()
