"""SqlExpert application icon, variant T2 (current): the T drawing with larger squares.
Run from this folder: python genT2.py -> sqlexpert-T2.svg, tuned/sqlexpert-T2-48.svg, out/png/*.png and out/sqlexpert.ico
(copy out/sqlexpert.ico to SqlExpert.App/Assets/sqlexpert.ico). Sizes 16-40 are drawn pixel by pixel."""
import math, io, os
from PIL import Image
import cairosvg
import genT
from genT import svgT, FONT16, FONT20, FONT24

def tunedT2(size):
    if size >= 64: return svgT(sq=58, ring=90, textw=108, stroke=11, sqstroke=5)
    return svgT(sq=62, ring=90, textw=104, stroke=15, sqstroke=7)   # 48 tier

# Pixel-art hexagon layout: (font, text y0, square size q, ring radius)
HEX = {40: (FONT20, 16, 9, 15.0), 32: (FONT16, 13, 7, 12.0)}
# Small sizes: SQL + a bar of 3 larger squares (the ring is not readable there)
BAR = {24: (FONT16, 5, 4, 4, [(5,14),(10,14),(15,14)]), 20: (FONT16, 2, 3, 4, [(3,13),(8,13),(13,13)]), 16: (FONT16, 1, 2, 3, [(3,11),(7,11),(11,11)])}

def _render(s, grid):
    N = (0x0F,0x17,0x24,255)
    im = Image.new("RGBA", (s, s), (0,0,0,0)); px = im.load()
    for (x, y) in grid:
        for dx in (-1,0,1):
            for dy in (-1,0,1):
                p = (x+dx, y+dy)
                if p not in grid and 0 <= p[0] < s and 0 <= p[1] < s: px[p] = N
    for p, c in grid.items():
        if 0 <= p[0] < s and 0 <= p[1] < s: px[p] = c
    return im

W = (255,255,255,255); B = (0x4A,0xA8,0xE8,255); BL = (0x7C,0xC4,0xF2,255); BD = (0x3A,0x78,0xC2,255)
def _text(grid, font, x0, y0):
    x = x0
    for ch in "SQL":
        rows = font[ch]
        for r, line in enumerate(rows):
            for c, v in enumerate(line):
                if v == "#": grid[(x+c, y0+r)] = W
        x += len(rows[0]) + 1
def _textw(font): return sum(len(font[c][0]) for c in "SQL") + 2

def _square(grid, sx, sy, q):
    for dx in range(q):
        for dy in range(q):
            c = B
            if q >= 5 and dy == 0: c = BL
            if q >= 5 and dy == q-1: c = BD
            grid[(sx+dx, sy+dy)] = c

def pixelT2(s):
    grid = {}
    if s in HEX:
        font, y0, q, ring = HEX[s]
        _text(grid, font, (s - _textw(font) + 1)//2, y0)
        c = s/2
        for adeg in (0, -60, -120, 180, 60):
            a = math.radians(adeg)
            cx, cy = c + ring*math.cos(a), c + ring*math.sin(a)
            _square(grid, round(cx - q/2), round(cy - q/2), q)
    else:
        font, x0, y0, q, sqs = BAR[s]
        _text(grid, font, x0, y0)
        for (sx, sy) in sqs: _square(grid, sx, sy, q)
    return _render(s, grid)

def iconT2(s):
    if s in HEX or s in BAR: return pixelT2(s)
    if s < 48:
        src = next(t for t in (16,20,24,32,40,48) if t >= s)
        return iconT2(src).resize((s, s), Image.LANCZOS)
    tier = 64 if s >= 64 else 48
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=tunedT2(tier).encode(), output_width=s, output_height=s))).convert("RGBA")

if __name__ == "__main__":
    SIZES = [16,20,24,32,40,48,64,128,256]
    os.makedirs("out/png", exist_ok=True); os.makedirs("tuned", exist_ok=True)
    open("sqlexpert-T2.svg","w").write(tunedT2(256))
    open("tuned/sqlexpert-T2-48.svg","w").write(tunedT2(48))
    imgs = []
    for s in SIZES:
        im = iconT2(s); im.save(f"out/png/sqlexpert-T2-{s}.png"); imgs.append(im)
    imgs[-1].save("out/sqlexpert.ico", format="ICO", sizes=[(s,s) for s in SIZES], append_images=imgs[:-1], bitmap_format="bmp")
    print("ok")
