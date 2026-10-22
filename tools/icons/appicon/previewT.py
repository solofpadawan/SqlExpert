"""Preview sheet of the app icon on light/dark/blue backgrounds, desktop and taskbar (out/sqlexpert-appicon-preview.png)."""
import os
from PIL import Image, ImageDraw, ImageFont
from genT import iconT
F = lambda sz, b=False: ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf" % ("-Bold" if b else ""), sz)
W, H = 1500, 1300
im = Image.new("RGB", (W, H), "#E9ECF0"); d = ImageDraw.Draw(im)
d.text((30, 22), "SqlExpert — app icon, variant T (transparent background)", fill="#1A2230", font=F(30, True))
d.text((30, 62), "White 'SQL' with navy outline + soft shadow · 5 blue squares (hexagon, bottom-left empty) · 16/20/24/32/40 pixel-tuned", fill="#3A4658", font=F(16))
def wall(w, h, kind):
    t = Image.new("RGB", (w, h)); td = ImageDraw.Draw(t)
    for y in range(h):
        if kind == "blue": c = (40 + y*30//h, 80 + y*40//h, 140 + y*40//h)
        elif kind == "light": c = (240, 242, 245)
        else: c = (28, 28, 30)
        td.line([(0, y), (w, y)], fill=c)
    return t
SIZES = [256, 64, 48, 40, 32, 24, 20, 16]
rows = (("Light", "light", "#333"), ("Dark", "dark", "#CCC"), ("Blue wallpaper", "blue", "#E6ECF5"))
y = 100
for name, kind, fg in rows:
    im.paste(wall(W-60, 290, kind), (30, y))
    d.text((44, y+8), name, fill=fg, font=F(15, True))
    x = 50
    for s in SIZES:
        ic = iconT(s); im.paste(ic, (x, y+8+(256-s)), ic)
        d.text((x, y+270), f"{s}px", fill=fg, font=F(13)); x += s+28
    for s in (32, 24, 16):
        z = iconT(s).resize((s*4, s*4), Image.NEAREST); im.paste(z, (x, y+8+(256-s*4)), z)
        d.text((x, y+270), f"{s}px ×4", fill=fg, font=F(13)); x += s*4+24
    y += 300
# desktop shortcuts + taskbars
def arrow(sz):
    a = Image.new("RGBA", (sz, sz), (0,0,0,0)); ad = ImageDraw.Draw(a)
    ad.rectangle([0, 0, sz-1, sz-1], fill=(245,245,245,255), outline=(120,120,120,255))
    m = sz/10
    ad.polygon([(3*m,8*m),(3*m,5*m),(5.5*m,3*m),(4.5*m,2*m),(8*m,2*m),(8*m,5.5*m),(7*m,4.5*m),(4.5*m,6.5*m),(4.5*m,8*m)], fill=(30,90,170,255))
    return a
for k, kind in enumerate(("blue", "light", "dark")):
    bx = 30 + k*480; im.paste(wall(460, 120, kind), (bx, y))
    fg = "#000" if kind == "light" else "#FFF"
    for j, (s, fs, lab) in enumerate(((48, 12, "100%"), (72, 17, "150%"))):
        ix = bx + 30 + j*200; ic = iconT(s); im.paste(ic, (ix, y+6), ic)
        ar = arrow(s*10//48 + 4); im.paste(ar, (ix, y+6+s-ar.height), ar)
        t = "SqlExpert"; tw = d.textlength(t, font=F(fs))
        if kind != "light": d.text((ix+s/2-tw/2+1, y+10+s+1), t, fill="#000", font=F(fs))
        d.text((ix+s/2-tw/2, y+10+s), t, fill=fg, font=F(fs))
        d.text((ix+s+10, y+20), f"desktop {lab}", fill=("#555" if kind == "light" else "#DDE"), font=F(12))
y += 140
for k, (s, h, lab) in enumerate(((24, 40, "100%"), (36, 60, "150%"))):
    for m, (bar, hov, ph, txt) in enumerate((("#202020", "#3A3A3A", "#777", "#DDD"), ("#F3F3F3", "#E0E0E0", "#AAA", "#333"))):
        tx = 30 + m*720; ty = y + k*75; bw = 560
        d.rectangle([tx, ty, tx+bw, ty+h], fill=bar); cx = tx+14
        for j in range(6):
            if j == 2:
                d.rounded_rectangle([cx-4, ty+3, cx+s+4, ty+h-3], radius=4, fill=hov)
                ic = iconT(s); im.paste(ic, (cx, ty+(h-s)//2), ic)
                d.line([cx+s/2-6, ty+h-3, cx+s/2+6, ty+h-3], fill="#4AA8E8", width=3)
            else:
                d.rounded_rectangle([cx+3, ty+(h-s)//2+3, cx+s-3, ty+(h+s)//2-3], radius=3, fill=ph)
            cx += s+22
        d.text((tx+bw-140, ty+h//2-8), f"taskbar {lab}", fill=txt, font=F(13))
im = im.crop((0, 0, W, y+150)); os.makedirs("out", exist_ok=True); im.save("out/sqlexpert-appicon-preview.png"); print(im.size)
