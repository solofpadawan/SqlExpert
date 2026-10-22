"""Glyph outlines for the app icon: "SQL" as SVG path data (Inter Tight, weight 800, SIL Open Font License).
Set SQLEXPERT_ICON_FONT to the variable font file if it is somewhere else."""
import math, io, os
from fontTools.ttLib import TTFont
from fontTools.varLib import instancer
from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.boundsPen import BoundsPen
from fontTools.pens.transformPen import TransformPen

FONT = os.environ.get("SQLEXPERT_ICON_FONT", "/usr/share/fonts/truetype/sand-box/google/Inter Tight/InterTight-VariableFont_wght.ttf")
f = TTFont(FONT)
f = instancer.instantiateVariableFont(f, {"wght": 800})
gs = f.getGlyphSet(); cmap = f.getBestCmap(); hmtx = f["hmtx"]

def text_path(txt, cx, cy, width, track=0.0):
    """Return SVG path d for txt, scaled to given width, centered at cx,cy (cap-centered)."""
    names = [cmap[ord(c)] for c in txt]
    # layout in font units
    x = 0; parts = []
    upm = f["head"].unitsPerEm
    for i, n in enumerate(names):
        parts.append((n, x)); x += hmtx[n][0] + (track*upm if i < len(names)-1 else 0)
    bp = BoundsPen(gs)
    for n, ox in parts:
        gs[n].draw(TransformPen(bp, (1,0,0,1,ox,0)))
    x0,y0,x1,y1 = bp.bounds
    s = width/(x1-x0)
    capc = (y0+y1)/2
    pen = SVGPathPen(gs)
    for n, ox in parts:
        # flip y, scale, translate
        t = (s,0,0,-s, cx - (x0+(x1-x0)/2)*s + ox*s, cy + capc*s)
        gs[n].draw(TransformPen(pen, t))
    return pen.getCommands(), (y1-y0)*s
