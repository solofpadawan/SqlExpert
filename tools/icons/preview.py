#!/usr/bin/env python3
"""Renders the icon set (16 px and 32 px) plus a mock Explorer/toolbar strip into one PNG.
Needs cairosvg and Pillow:  python3 tools/icons/preview.py out.png"""
import io
import os
import sys

import cairosvg
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import generate_icons as g  # noqa: E402


def render(name, px):
    png = cairosvg.svg2png(bytestring=g.svg(g.ICONS[name], px).encode(), output_width=px, output_height=px)
    return Image.open(io.BytesIO(png)).convert("RGBA")


def grey(img):
    # Same idea as the app's disabled state: luminance grey, lighter, then 60% opacity.
    px = img.load()
    out = Image.new("RGBA", img.size)
    po = out.load()
    for y in range(img.size[1]):
        for x in range(img.size[0]):
            r, gg, b, a = px[x, y]
            lum = int(0.299 * r + 0.587 * gg + 0.114 * b)
            lum = int(lum + (255 - lum) * 0.35)
            po[x, y] = (lum, lum, lum, int(a * 0.6))
    return out


def font(size, bold=False):
    for f in ("DejaVuSans-Bold.ttf" if bold else "DejaVuSans.ttf",):
        for d in ("/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/dejavu"):
            p = os.path.join(d, f)
            if os.path.exists(p):
                return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def strip(scale):
    """Mock main toolbar + Database Explorer + windows bar at the given DPI scale (1 = 100%)."""
    s = scale
    w, h = 470 * s, 300 * s
    img = Image.new("RGBA", (w, h), "#F0F0F0")
    d = ImageDraw.Draw(img)
    f = font(12 * s)
    fb = font(12 * s, True)
    d.rectangle([0, 0, w, 26 * s], fill="#E8E8E8")
    groups = [["RegisterDatabase", "UnregisterDatabase"], ["ConnectDatabase", "DisconnectDatabase", "ReconnectDatabase"],
              ["DatabaseRegistrationInfo", "Exit"], ["SqlEditor", "NewSqlEditor", "QueryBuilder"],
              ["ScriptExecutive"], ["SqlMonitor", "ExtractMetadata", "PrintMetadata"]]
    x = 6 * s
    for gi, grp in enumerate(groups):
        for name in grp:
            ic = render(name, 16 * s)
            img.alpha_composite(ic, (x + 3 * s, 5 * s))
            x += 23 * s
        d.line([x + 2 * s, 5 * s, x + 2 * s, 21 * s], fill="#B0B0B0", width=max(1, s))
        x += 7 * s
    # disabled sample
    ic = grey(render("BlobViewer", 16 * s))
    img.alpha_composite(ic, (x + 3 * s, 5 * s))
    d.text((x + 24 * s, 7 * s), "(disabled)", font=font(10 * s), fill="#808080")

    d.rectangle([0, 30 * s, 300 * s, h - 30 * s], fill="white", outline="#C0C0C0")
    rows = [
        (0, "DatabaseRegistrationConnected", "SQL Server Test", None, True),
        (1, "Database", "master", None, False),
        (1, "Database", "SqlExpertTeste", None, False),
        (2, "UserDefinedType", "User-Defined Types", "(0)", False),
        (2, "Table", "Tables", "(5)", False),
        (3, "Table", "dbo.Clientes", None, False),
        (2, "View", "Views", "(2)", False),
        (2, "Procedure", "Procedures", "(3)", False),
        (3, "Procedure", "dbo.teste_proc_01", None, False),
        (2, "Function", "Functions", "(1)", False),
        (2, "Trigger", "Triggers", "(1)", False),
        (2, "Sequence", "Sequences", "(0)", False),
        (2, "Role", "Roles", "(10)", False),
        (0, "DatabaseRegistration", "Other server", None, False),
    ]
    y = 36 * s
    for depth, icon, text, count, bold in rows:
        xx = (8 + depth * 16) * s
        img.alpha_composite(render(icon, 16 * s), (xx, y))
        d.text((xx + 20 * s, y + 1 * s), text, font=fb if bold else f, fill="black")
        if count:
            tw = d.textlength(text, font=f)
            d.text((xx + 20 * s + tw + 4 * s, y + 1 * s), count, font=f, fill="#1F3FD0")
        y += 16 * s
    # windows bar
    d.rectangle([0, h - 26 * s, w, h], fill="#D4D4D4")
    x = 4 * s
    for icon, text in (("Table", "Clientes"), ("Procedure", "teste_proc_01"), ("SqlEditor", "SQL Editor 1")):
        bw = 120 * s
        d.rectangle([x, h - 23 * s, x + bw, h - 3 * s], fill="#E4E4E4", outline="#A0A0A0")
        img.alpha_composite(render(icon, 16 * s), (x + 4 * s, h - 21 * s))
        d.text((x + 24 * s, h - 20 * s), text, font=f, fill="black")
        x += bw + 4 * s
    return img


def main():
    out = sys.argv[1] if len(sys.argv) > 1 else "sqlexpert-icons-preview.png"
    names = list(g.ICONS)
    cols = 4
    cell_w, cell_h = 250, 44
    rows = (len(names) + cols - 1) // cols
    s1, s2 = strip(1), strip(2)
    W = max(cols * cell_w + 20, s1.width + s2.width + 30)
    H = 40 + rows * cell_h + 30 + s2.height + 20
    sheet = Image.new("RGBA", (W, H), "white")
    d = ImageDraw.Draw(sheet)
    d.text((10, 10), "SqlExpert icons - 16 px | 32 px | 16 px disabled", font=font(14, True), fill="black")
    for i, name in enumerate(names):
        cx = 10 + (i % cols) * cell_w
        cy = 40 + (i // cols) * cell_h
        sheet.alpha_composite(render(name, 16), (cx, cy + 8))
        sheet.alpha_composite(render(name, 32), (cx + 22, cy))
        sheet.alpha_composite(grey(render(name, 16)), (cx + 60, cy + 8))
        d.text((cx + 82, cy + 10), name, font=font(11), fill="#202020")
    y = 40 + rows * cell_h + 10
    d.text((10, y), "Mock at 100% (left) and 200% (right)", font=font(13, True), fill="black")
    sheet.alpha_composite(s1, (10, y + 22))
    sheet.alpha_composite(s2, (20 + s1.width, y + 22))
    sheet.convert("RGB").save(out)
    print(out, sheet.size)


if __name__ == "__main__":
    main()
