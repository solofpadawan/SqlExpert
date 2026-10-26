from PIL import Image, ImageDraw, ImageFont
from genT import iconT
from genT2 import iconT2
F = lambda sz, b=False: ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans%s.ttf" % ("-Bold" if b else ""), sz)
W, H = 1240, 760
im = Image.new("RGB", (W, H), "#E9ECF0"); d = ImageDraw.Draw(im)
d.text((30, 20), "SqlExpert icon — T (current) vs T2 (larger squares)", fill="#1A2230", font=F(28, True))
def arrow(sz):
    a = Image.new("RGBA", (sz, sz), (0,0,0,0)); ad = ImageDraw.Draw(a)
    ad.rectangle([0, 0, sz-1, sz-1], fill=(245,245,245,255), outline=(120,120,120,255)); m = sz/10
    ad.polygon([(3*m,8*m),(3*m,5*m),(5.5*m,3*m),(4.5*m,2*m),(8*m,2*m),(8*m,5.5*m),(7*m,4.5*m),(4.5*m,6.5*m),(4.5*m,8*m)], fill=(30,90,170,255))
    return a
# Desktop strip (black background), 48 px and 32 px, actual size + 3x zoom
d.rectangle([30, 70, W-30, 330], fill="#000000")
d.text((44, 78), "Desktop (dark) — actual size and 3× zoom", fill="#CCC", font=F(14, True))
x = 50
for lab, f in (("T", iconT), ("T2", iconT2)):
    for s in (48, 32):
        ic = f(s); ix, iy = x, 120
        im.paste(ic, (ix, iy), ic); ar = arrow(s*10//48 + 4); im.paste(ar, (ix, iy+s-ar.height), ar)
        t = "SqlExpert"; tw = d.textlength(t, font=F(12)); d.text((ix+s/2-tw/2, iy+s+4), t, fill="#FFF", font=F(12))
        d.text((ix+s/2-12, iy+s+22), f"{lab} {s}", fill="#8AB", font=F(12))
        x += 90
x += 20
for lab, f in (("T", iconT), ("T2", iconT2)):
    for s in (48, 32):
        z = f(s).resize((s*3, s*3), Image.NEAREST); im.paste(z, (x, 110), z)
        d.text((x, 110+s*3+6), f"{lab} {s}px ×3", fill="#8AB", font=F(12)); x += s*3 + 18
# 256 side by side on light and dark
for k, (bg, fg) in enumerate((("#1C1C1E", "#CCC"), ("#F0F2F5", "#333"))):
    bx = 30 + k*600
    d.rectangle([bx, 350, bx+580, 740], fill=bg)
    for j, (lab, f) in enumerate((("T (current)", iconT), ("T2 (new)", iconT2))):
        ic = f(256); im.paste(ic, (bx+20+j*290, 370), ic)
        d.text((bx+20+j*290, 640), f"{lab} 256px", fill=fg, font=F(14, True))
    for j, (lab, f) in enumerate((("T", iconT), ("T2", iconT2))):
        for i, s in enumerate((64, 48, 32, 24, 16)):
            ic = f(s); px = bx+20+j*290 + sum((64, 48, 32, 24, 16)[:i]) + i*8; im.paste(ic, (px, 670+64-s), ic)
import os; os.makedirs("out", exist_ok=True); im.save("out/sqlexpert-appicon-preview-T2.png"); print(im.size)
