"""SqlExpert application icon (transparent background): white "SQL" with a navy outline and five blue squares.
Run from this folder: python genT.py -> sqlexpert-T.svg, tuned/*.svg, out/png/*.png and out/sqlexpert.ico
(copy out/sqlexpert.ico to SqlExpert.App/Assets/sqlexpert.ico). Sizes 16-40 are pixel-tuned."""
import math, io
from PIL import Image
import os
import cairosvg
from textpath import text_path

NAVY = "#0F1724"
DEFS = '''<defs>
 <linearGradient id="sq" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#7CC4F2"/><stop offset="0.45" stop-color="#4AA8E8"/><stop offset="1" stop-color="#3A78C2"/>
 </linearGradient>
 <linearGradient id="txt" x1="0" y1="0" x2="0" y2="1">
  <stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#E2EAF4"/>
 </linearGradient>
</defs>'''

def svgT(sq=32, ring=100, textw=124, stroke=11, sqstroke=4, shadow=True, flat=False, n=5, ty=128, bar=False, track=0.0):
    o = ['<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256">', DEFS]
    sqfill = "#4AA8E8" if flat else "url(#sq)"
    txfill = "#FFFFFF" if flat else "url(#txt)"
    def square(x, y, s):
        r = s*0.24
        if shadow:
            for k, op in ((sqstroke*2.0, .07), (sqstroke*1.3, .12)):
                o.append(f'<rect x="{x-s/2:.2f}" y="{y-s/2+4:.2f}" width="{s:.2f}" height="{s:.2f}" rx="{r:.2f}" fill="none" stroke="#000" stroke-opacity="{op}" stroke-width="{k:.2f}"/>')
        o.append(f'<rect x="{x-s/2:.2f}" y="{y-s/2:.2f}" width="{s:.2f}" height="{s:.2f}" rx="{r:.2f}" fill="{sqfill}" stroke="#1D3E66" stroke-width="{sqstroke}"/>')
        if not flat:  # top highlight
            o.append(f'<rect x="{x-s/2+s*0.18:.2f}" y="{y-s/2+s*0.12:.2f}" width="{s*0.64:.2f}" height="{s*0.16:.2f}" rx="{s*0.08:.2f}" fill="#FFFFFF" opacity="0.35"/>')
    if n == 5:
        for adeg in (0, -60, -120, 180, 60):
            a = math.radians(adeg); square(128+ring*math.cos(a), 128+ring*math.sin(a), sq)
    if textw:
        d, h = text_path("SQL", 128, ty, textw, track)
        if shadow:
            ds, _ = text_path("SQL", 128, ty+5, textw, track)
            for k, op in ((stroke*1.9, .07), (stroke*1.4, .12)):
                o.append(f'<path d="{ds}" fill="#000" fill-opacity="{op}" stroke="#000" stroke-opacity="{op}" stroke-width="{k:.1f}" stroke-linejoin="round"/>')
        o.append(f'<path d="{d}" fill="{NAVY}" stroke="{NAVY}" stroke-width="{stroke}" stroke-linejoin="round"/>')
        o.append(f'<path d="{d}" fill="{txfill}"/>')
        if bar:
            q = 34; gap = 18; y = ty + h/2 + 22
            for k in (-1, 0, 1):
                x = 128 + k*(q+gap) - q/2
                o.append(f'<rect x="{x-4:.1f}" y="{y-4:.1f}" width="{q+8}" height="{q+8}" rx="9" fill="{NAVY}"/>')
                o.append(f'<rect x="{x:.1f}" y="{y:.1f}" width="{q}" height="{q}" rx="6" fill="{sqfill}"/>')
    o.append('</svg>'); return "\n".join(o)

def tunedT(size):
    if size >= 64: return svgT()
    if size >= 40: return svgT(sq=38, ring=94, textw=116, stroke=14, sqstroke=6)
    if size >= 32: return svgT(sq=44, ring=96, textw=114, stroke=18, sqstroke=8, shadow=False, flat=True)
    return svgT(n=0, textw=210, stroke=22, shadow=False, flat=True, ty=96, bar=True, track=0.02)

# Pixel art for 16/20: 1px navy outline around white glyph pixels and blue squares
FONT16 = g_font16 = {"S":[".###","#...","#...",".##.","...#","###."],
          "Q":[".##.","#..#","#..#","#..#","#..#",".##.","...#"],
          "L":["#...","#...","#...","#...","#...","####"]}
FONT20 = {"S":[".####","##...","##...",".###.","...##","...##","####."],
          "Q":[".###.","##.##","##.##","##.##","##.##","##.##",".###.","...##"],
          "L":["##...","##...","##...","##...","##...","##...","#####"]}
FONT24 = {"S":[".####.","##..##","##....",".####.","....##","##..##",".####."],
          "Q":[".####.","##..##","##..##","##..##","##..##","##..##",".####.","....##"],
          "L":["##....","##....","##....","##....","##....","##....","######"]}
PIX = {40:(FONT24,10,16,5,[(10,5),(25,5),(2,18),(33,18),(25,30)]), 32:(FONT20,8,12,4,[(8,4),(20,4),(2,14),(26,14),(20,24)]), 24:(FONT24,2,4,3,[(6,15),(11,15),(16,15)]), 16:(FONT16,1,3,2,[(4,12),(7,12),(10,12)]), 20:(FONT20,2,3,2,[(5,15),(9,15),(13,15)])}
def pixelT(s):
    font, x0, y0, q, sqs = PIX[s]
    W = (255,255,255,255); B = (0x4A,0xA8,0xE8,255); N = (0x0F,0x17,0x24,255)
    grid = {}; x = x0
    for ch in "SQL":
        rows = font[ch]
        for r, line in enumerate(rows):
            for c, v in enumerate(line):
                if v == "#": grid[(x+c, y0+r)] = W
        x += len(rows[0]) + 1
    for (sx, sy) in sqs:
        for dx in range(q):
            for dy in range(q): grid[(sx+dx, sy+dy)] = B
    im = Image.new("RGBA", (s, s), (0,0,0,0)); px = im.load()
    for (x, y) in grid:  # outline (8-neighbour)
        for dx in (-1,0,1):
            for dy in (-1,0,1):
                p = (x+dx, y+dy)
                if p not in grid and 0 <= p[0] < s and 0 <= p[1] < s: px[p] = N
    for p, c in grid.items(): px[p] = c
    return im

def iconT(s):
    if s in PIX: return pixelT(s)
    if s < 48:  # not an .ico size: Windows scales the next larger frame
        src = next(t for t in (16,20,24,32,40,48) if t >= s)
        return iconT(src).resize((s, s), Image.LANCZOS)
    tier = next(t for t in (64,48) if s >= t)
    return Image.open(io.BytesIO(cairosvg.svg2png(bytestring=tunedT(tier).encode(), output_width=s, output_height=s))).convert("RGBA")

if __name__ == "__main__":
    SIZES = [16,20,24,32,40,48,64,128,256]
    os.makedirs("out/png", exist_ok=True); os.makedirs("tuned", exist_ok=True)
    open("sqlexpert-T.svg","w").write(svgT())
    for s in (24,32,48): open(f"tuned/sqlexpert-T-{s}.svg","w").write(tunedT(s))
    imgs = []
    for s in SIZES:
        im = iconT(s); im.save(f"out/png/sqlexpert-T-{s}.png"); imgs.append(im)
    imgs[-1].save("out/sqlexpert.ico", format="ICO", sizes=[(s,s) for s in SIZES], append_images=imgs[:-1], bitmap_format="bmp")
    print("ok")
