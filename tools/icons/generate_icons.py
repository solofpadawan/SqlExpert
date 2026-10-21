#!/usr/bin/env python3
"""
SqlExpert icon set: original vector drawings on a 16x16 grid.

Single source of truth for every UI icon. Running this script writes
SqlExpert.App/Themes/Icons.xaml (WPF DrawingImage resources "Icon.<Name>") and,
with --svg DIR, one SVG per icon (used to render the preview sheet).

Style rules: simple shapes, 1px outlines on half-pixel coordinates (crisp at 100%),
soft muted colors, no green. Badges (plus, minus, bolt, ...) sit bottom-right with a
white halo so they stay readable over the base drawing.
"""
import math
import os
import sys

# ---------------------------------------------------------------- palette (muted, no green)
BLUE = dict(light="#DDE9F8", mid="#8FB3E0", head="#6E9AD3", dark="#3B6AA8", deep="#2F5A94")
AMBER = dict(light="#F6D9A8", mid="#E5A645", dark="#9C6416")
SLATE = dict(light="#E3E9F0", mid="#7D9BBA", soft="#A9BCD0", dark="#43607E", deep="#2E4560")
PURPLE = dict(light="#ECE5F5", mid="#9B82C2", dark="#5E4589")
YELLOW = dict(fill="#F2CF5B", dark="#A2801A")
TEAL = dict(light="#D9EEF0", mid="#6FB2B9", dark="#33808A", deep="#2A6E77")
BRICK = dict(light="#E7B7AC", mid="#B9604F", dark="#7C3A2E")
GREY = dict(light="#EEEEEE", mid="#9A9A9A", dark="#6B6B6B", glyph="#555555")
SAND = dict(light="#F7EBC8", mid="#EBD69C", dark="#8C6B1F")
RED = dict(fill="#C0473A", dark="#8A2E25")
PAPER = dict(fill="#FFFFFF", line="#6B7C93")
WOOD = dict(fill="#D9B48A", dark="#7A5230")
WHITE = "#FFFFFF"


def n(v):
    s = f"{v:.2f}".rstrip("0").rstrip(".")
    return "0" if s == "-0" else s


def shape(d, fill=None, stroke=None, w=1.0):
    return dict(d=d, fill=fill, stroke=stroke, w=w)


def rect(x1, y1, x2, y2):
    return f"M{n(x1)},{n(y1)} H{n(x2)} V{n(y2)} H{n(x1)} Z"


def rrect(x1, y1, x2, y2, r):
    return (f"M{n(x1 + r)},{n(y1)} H{n(x2 - r)} A{n(r)},{n(r)} 0 0 1 {n(x2)},{n(y1 + r)} V{n(y2 - r)} "
            f"A{n(r)},{n(r)} 0 0 1 {n(x2 - r)},{n(y2)} H{n(x1 + r)} A{n(r)},{n(r)} 0 0 1 {n(x1)},{n(y2 - r)} "
            f"V{n(y1 + r)} A{n(r)},{n(r)} 0 0 1 {n(x1 + r)},{n(y1)} Z")


def circle(cx, cy, r):
    return f"M{n(cx - r)},{n(cy)} A{n(r)},{n(r)} 0 1 0 {n(cx + r)},{n(cy)} A{n(r)},{n(r)} 0 1 0 {n(cx - r)},{n(cy)} Z"


def pixels(rows, x0, y0, color):
    """Pixel glyph: rows of '#'/'.' drawn as integer-aligned filled runs."""
    parts = []
    for j, row in enumerate(rows):
        i = 0
        while i < len(row):
            if row[i] == "#":
                k = i
                while k < len(row) and row[k] == "#":
                    k += 1
                parts.append(rect(x0 + i, y0 + j, x0 + k, y0 + j + 1))
                i = k
            else:
                i += 1
    return shape(" ".join(parts), fill=color)


# ---------------------------------------------------------------- base drawings

def table(p=BLUE):
    return [
        shape(rect(1.5, 1.5, 14.5, 14.5), fill=p["light"], stroke=p["dark"]),
        shape(rect(2, 2, 14, 5), fill=p["head"]),
        shape("M1.5,5.5 H14.5", stroke=p["dark"]),
        shape("M2,8.5 H14 M2,11.5 H14 M5.5,6 V14 M10.5,6 V14", stroke=p["mid"]),
    ]


def gear():
    cx = cy = 8.0
    teeth, ro, ri = 8, 7.0, 5.3
    pts = []
    for t in range(teeth):
        a = 2 * math.pi * t / teeth
        half_top, half_root = math.radians(9), math.radians(15)
        for ang, r in ((a - half_root - math.radians(7), ri), (a - half_top, ro), (a + half_top, ro), (a + half_root + math.radians(7), ri)):
            pts.append((cx + r * math.cos(ang), cy + r * math.sin(ang)))
    d = "M" + " L".join(f"{n(x)},{n(y)}" for x, y in pts) + " Z"
    return [
        shape(d, fill=AMBER["mid"], stroke=AMBER["dark"]),
        shape(circle(8, 8, 2.2), fill=WHITE, stroke=AMBER["dark"]),
    ]


def eye():
    return [
        shape("M0.5,8 Q8,0.5 15.5,8 Q8,15.5 0.5,8 Z", fill=WHITE, stroke=SLATE["dark"]),
        shape(circle(8, 8, 3.2), fill=SLATE["mid"], stroke=SLATE["dark"]),
        shape(circle(8, 8, 1.5), fill=SLATE["deep"]),
        shape(circle(9.2, 6.8, 0.7), fill=WHITE),
    ]


def fx():
    return [
        shape(rrect(1.5, 1.5, 14.5, 14.5, 2), fill=PURPLE["light"], stroke=PURPLE["dark"]),
        shape("M8.2,3.9 C7,3.3 6,3.9 5.8,5.2 L4.9,11.4 C4.7,12.6 3.9,13 3.1,12.6 M3.9,7.3 H7.6", stroke=PURPLE["dark"], w=1.4),
        shape("M9,7.5 L12.8,12.5 M12.8,7.5 L9,12.5", stroke=PURPLE["dark"], w=1.4),
    ]


def bolt(fill=YELLOW["fill"], dark=YELLOW["dark"]):
    return [shape("M9.5,0.5 L2.5,9 H7.5 L6,15.5 L13.5,6.5 H8.5 Z", fill=fill, stroke=dark)]


def sequence():
    p = TEAL
    one = [".#", "##", ".#", ".#", ".#"]
    two = ["###", "..#", "###", "#..", "###"]
    three = ["###", "..#", "###", "..#", "###"]
    return [
        shape(rect(4.5, 1.5, 14.5, 10.5), fill=p["light"], stroke=p["dark"]),
        shape(rect(3, 3.5, 13.5, 12.5), fill=p["light"], stroke=p["dark"]),
        shape(rect(1.5, 5.5, 12.5, 14.5), fill=WHITE, stroke=p["dark"]),
        pixels(one, 2, 8, p["deep"]),
        pixels(two, 5, 8, p["deep"]),
        pixels(three, 9, 8, p["deep"]),
    ]


def people():
    return [
        shape(circle(10.8, 4.3, 2.4), fill=BRICK["light"], stroke=BRICK["dark"]),
        shape("M6.8,13.5 C6.8,10 8.6,8.4 10.8,8.4 C13,8.4 14.8,10 14.8,13.5 Z", fill=BRICK["light"], stroke=BRICK["dark"]),
        shape(circle(5.8, 6.3, 2.6), fill=BRICK["mid"], stroke=BRICK["dark"]),
        shape("M1.2,15 C1.2,11.6 3.3,10.2 5.8,10.2 C8.3,10.2 10.4,11.6 10.4,15 Z", fill=BRICK["mid"], stroke=BRICK["dark"]),
    ]


def person():
    return [
        shape(circle(8, 5, 3), fill=SLATE["soft"], stroke=SLATE["dark"]),
        shape("M2.5,15 C2.5,10.8 5,9.3 8,9.3 C11,9.3 13.5,10.8 13.5,15 Z", fill=SLATE["soft"], stroke=SLATE["dark"]),
    ]


def type_t():
    return [
        shape(rrect(1.5, 1.5, 14.5, 14.5, 2), fill=GREY["light"], stroke=GREY["dark"]),
        shape("M4,4 H12 V6 H9 V12 H7 V6 H4 Z", fill=GREY["glyph"]),
    ]


def cylinder(fill, top, dark, band=None):
    return [
        shape("M2.5,3.5 V12.5 A5.5,2 0 0 0 13.5,12.5 V3.5 Z", fill=fill, stroke=dark),
        shape("M2.5,8 A5.5,2 0 0 0 13.5,8", stroke=band or dark),
        shape("M2.5,3.5 A5.5,2 0 1 1 13.5,3.5 A5.5,2 0 1 1 2.5,3.5 Z", fill=top, stroke=dark),
    ]


def db_registered():
    return cylinder(SAND["mid"], SAND["light"], SAND["dark"])


def db_connected():
    return cylinder(BLUE["head"], BLUE["light"], BLUE["deep"])


def db_child():
    return cylinder("#CFDDEE", "#EEF3FA", "#56789F")


def page(lines=True, colored=True):
    s = [
        shape("M2.5,1.5 H10 L13.5,5 V14.5 H2.5 Z", fill=PAPER["fill"], stroke=PAPER["line"]),
        shape("M10,1.5 V5 H13.5", stroke=PAPER["line"]),
    ]
    if lines:
        if colored:
            s += [shape("M4.5,6.5 H11.5 M4.5,10.5 H10.5", stroke=BLUE["dark"]),
                  shape("M4.5,8.5 H8.5 M4.5,12.5 H11.5", stroke=BRICK["mid"])]
        else:
            s += [shape("M4.5,6.5 H11.5 M4.5,8.5 H10.5 M4.5,10.5 H11.5 M4.5,12.5 H9.5", stroke=SLATE["mid"])]
    return s


def sql_page():
    return page(lines=True, colored=True)


def query_builder():
    p = BLUE
    return [
        shape("M7.5,4.5 H11.5 V8.5", stroke=p["dark"]),
        shape(rect(1.5, 1.5, 7.5, 8.5), fill=p["light"], stroke=p["dark"]),
        shape(rect(2, 2, 7, 4), fill=p["head"]),
        shape(rect(8.5, 8.5, 14.5, 14.5), fill=p["light"], stroke=p["dark"]),
        shape(rect(9, 9, 14, 11), fill=p["head"]),
    ]


def monitor():
    return [
        shape(rect(1.5, 2.5, 14.5, 11.5), fill=SLATE["light"], stroke=SLATE["dark"]),
        shape("M8,11.5 V14 M5,14.5 H11", stroke=SLATE["dark"]),
        shape("M3,8 H5.5 L7,4.5 L9,10 L10.5,7 H13", stroke=RED["fill"]),
    ]


def printer():
    return [
        shape(rect(4.5, 1.5, 11.5, 5.5), fill=WHITE, stroke=SLATE["dark"]),
        shape(rect(1.5, 5.5, 14.5, 11.5), fill="#D5DCE5", stroke=SLATE["dark"]),
        shape(rect(4.5, 9.5, 11.5, 14.5), fill=WHITE, stroke=SLATE["dark"]),
        shape("M6,11.5 H10 M6,13 H9", stroke=SLATE["mid"]),
        shape(rect(12, 7, 13, 8), fill=AMBER["mid"]),
    ]


def key(p=AMBER):
    return [
        shape("M7.2,7.2 L14,14 M11.3,11.3 L12.8,9.8 M13,13 L14.4,11.6", stroke=p["dark"], w=1.6),
        shape(circle(5, 5, 3.6), fill=p["mid"], stroke=p["dark"]),
        shape(circle(4.3, 4.3, 1.1), fill=WHITE),
    ]


def report():
    return page(lines=False) + [
        shape(rect(4, 9, 6, 13), fill=BLUE["head"]),
        shape(rect(7, 6, 9, 13), fill=BLUE["dark"]),
        shape(rect(10, 8, 12, 13), fill=BLUE["mid"]),
    ]


def picture():
    return [
        shape(rect(1.5, 2.5, 14.5, 13.5), fill=WHITE, stroke=GREY["dark"]),
        shape("M2,13 L6,7.5 L9,11 L11,9 L14,13 Z", fill=BLUE["mid"]),
        shape(circle(11, 6, 1.5), fill=AMBER["mid"]),
    ]


def folder_open():
    return [
        shape("M1.5,13.5 V2.5 H5.5 L7,4 H13.5 V13.5 Z", fill=SAND["mid"], stroke=SAND["dark"]),
        shape("M1.5,13.5 L3.5,7.5 H15.5 L13.5,13.5 Z", fill=SAND["light"], stroke=SAND["dark"]),
    ]


def floppy():
    return [
        shape("M1.5,1.5 H12 L14.5,4 V14.5 H1.5 Z", fill=BLUE["head"], stroke=BLUE["deep"]),
        shape(rect(4, 9, 12, 14), fill=WHITE),
        shape("M5,10.5 H11 M5,12.5 H11", stroke=BLUE["mid"]),
        shape(rect(4.5, 1.5, 10.5, 5.5), fill=BLUE["light"], stroke=BLUE["deep"]),
        shape(rect(8, 2.5, 9.5, 4.5), fill=BLUE["deep"]),
    ]


def scissors():
    c = SLATE["dark"]
    return [
        shape("M6,10.6 L11.2,1.8 M10,10.6 L4.8,1.8", stroke=c, w=1.3),
        shape(circle(4.5, 12.3, 2.2), fill=WHITE, stroke=c, w=1.2),
        shape(circle(11.5, 12.3, 2.2), fill=WHITE, stroke=c, w=1.2),
    ]


def copy_pages():
    return [
        shape(rect(5.5, 1.5, 14.5, 11.5), fill=WHITE, stroke=PAPER["line"]),
        shape(rect(1.5, 4.5, 10.5, 14.5), fill=WHITE, stroke=PAPER["line"]),
        shape("M3.5,7.5 H8.5 M3.5,9.5 H8.5 M3.5,11.5 H7.5", stroke=SLATE["mid"]),
    ]


def clipboard():
    return [
        shape(rect(2.5, 2.5, 13.5, 14.5), fill=WOOD["fill"], stroke=WOOD["dark"]),
        shape(rect(4.5, 5.5, 11.5, 13.5), fill=WHITE, stroke=PAPER["line"]),
        shape("M6,8 H10 M6,10 H10 M6,12 H9", stroke=SLATE["mid"]),
        shape(rect(5.5, 1.5, 10.5, 4.5), fill="#9AA6B5", stroke="#4A5563"),
    ]


def magnifier(cx=6.5, cy=6.5, r=4.5):
    c = SLATE["dark"]
    h = r * 0.72
    return [
        shape(f"M{n(cx + h)},{n(cy + h)} L14.5,14.5", stroke=c, w=2.4),
        shape(circle(cx, cy, r), fill="#EEF3FA", stroke=c, w=1.4),
    ]


def replace_arrows():
    return [
        shape("M2,5.5 H12.5 M10,3 L12.5,5.5 L10,8", stroke=BLUE["dark"], w=1.4),
        shape("M14,10.5 H3.5 M6,8 L3.5,10.5 L6,13", stroke=BRICK["mid"], w=1.4),
    ]


def warning():
    return [
        shape("M8,1.5 L15,14.5 H1 Z", fill=AMBER["light"], stroke=AMBER["dark"]),
        shape(rect(7, 5, 9, 10) + " " + rect(7, 11, 9, 13), fill=BRICK["dark"]),
    ]


def schema():
    c = SLATE
    return [
        shape("M8,5.5 V8 M4,10.5 V8 H12 V10.5", stroke=c["dark"]),
        shape(rect(5.5, 1.5, 10.5, 5.5), fill=c["soft"], stroke=c["dark"]),
        shape(rect(1.5, 10.5, 6.5, 14.5), fill=c["light"], stroke=c["dark"]),
        shape(rect(9.5, 10.5, 14.5, 14.5), fill=c["light"], stroke=c["dark"]),
    ]


def index_icon():
    return [
        shape("M1.5,3.5 H8.5 M1.5,7.5 H7 M1.5,11.5 H5.5", stroke=SLATE["dark"], w=1.0),
        shape("M12,2 V13.5 M9.5,11 L12,13.5 L14.5,11", stroke=BLUE["dark"], w=1.4),
    ]


def padlock():
    return [
        shape("M5,7.5 V5 A3,3 0 0 1 11,5 V7.5", stroke=SLATE["dark"], w=1.4),
        shape(rect(2.5, 7.5, 13.5, 14.5), fill=AMBER["mid"], stroke=AMBER["dark"]),
        shape(rect(7, 9.5, 9, 12.5), fill=AMBER["dark"]),
    ]


def chain():
    c = GREY["dark"]
    return [
        shape(rrect(1.5, 5.5, 9.5, 10.5, 2.5), stroke=c, w=1.4),
        shape(rrect(6.5, 5.5, 14.5, 10.5, 2.5), stroke=SLATE["dark"], w=1.4),
    ]


def door_exit():
    return [
        shape(rect(2.5, 1.5, 10.5, 14.5), fill=WOOD["fill"], stroke=WOOD["dark"]),
        shape(rect(7, 8, 8, 9), fill=WOOD["dark"]),
        shape("M8,8 H14 M11.5,5.5 L14,8 L11.5,10.5", stroke=WHITE, w=3.2),
        shape("M8,8 H14 M11.5,5.5 L14,8 L11.5,10.5", stroke=BLUE["deep"], w=1.5),
    ]


# navigator / editor actions
NAV = "#5A7090"


def tri_left(x1, x2):
    return shape(f"M{n(x2)},3 L{n(x1)},8 L{n(x2)},13 Z", fill=NAV, stroke=NAV)


def tri_right(x1, x2):
    return shape(f"M{n(x1)},3 L{n(x2)},8 L{n(x1)},13 Z", fill=NAV, stroke=NAV)


def plus_big():
    return [shape("M8,2.5 V13.5 M2.5,8 H13.5", stroke=BLUE["dark"], w=2.2)]


def minus_big():
    return [shape("M2.5,8 H13.5", stroke=BRICK["mid"], w=2.2)]


def check(color=BLUE["dark"]):
    return [shape("M2.5,8.5 L6.3,12.3 L13.5,4", stroke=color, w=2.2)]


def cross(color=BRICK["mid"]):
    return [shape("M3.5,3.5 L12.5,12.5 M12.5,3.5 L3.5,12.5", stroke=color, w=2.2)]


def pencil():
    return [
        shape("M2.5,13.5 L3.2,10.4 L10.8,2.8 L13.2,5.2 L5.6,12.8 Z", fill=AMBER["light"], stroke=AMBER["dark"]),
        shape("M9.4,4.2 L11.8,6.6", stroke=AMBER["dark"]),
        shape("M2.5,13.5 L3.2,10.4 L5.6,12.8 Z", fill=SLATE["dark"]),
    ]


def refresh(color=BLUE["dark"]):
    return [
        shape("M13,8.5 A5,5 0 1 1 10.8,4", stroke=color, w=1.6),
        shape("M9.2,1.6 L13.2,3.6 L9.8,6.4 Z", fill=color, stroke=color),
    ]


def play():
    return [shape("M4,2.5 L13.5,8 L4,13.5 Z", fill="#4A7CC0", stroke=BLUE["deep"])]


def stop():
    return [shape(rect(3.5, 3.5, 12.5, 12.5), fill=RED["fill"], stroke=RED["dark"])]


def undo():
    return [
        shape("M3,7.5 H10 A3.5,3.5 0 0 1 10,14.5 H6.5", stroke=BRICK["mid"], w=1.6),
        shape("M5.8,4 L2,7.5 L5.8,11 Z", fill=BRICK["mid"], stroke=BRICK["mid"]),
    ]


def fetch_all():
    return [
        shape("M8,1.5 V10.5 M4.5,7.5 L8,11 L11.5,7.5", stroke=BLUE["dark"], w=1.6),
        shape("M2.5,14 H13.5", stroke=SLATE["dark"], w=1.6),
    ]


def reorder():
    """Up arrow (blue) + down arrow (slate): change the order of a list."""
    return [
        shape("M5,13.5 V2.5 M2.5,5 L5,2.5 L7.5,5", stroke=BLUE["dark"], w=1.4),
        shape("M11,2.5 V13.5 M8.5,11 L11,13.5 L13.5,11", stroke=SLATE["dark"], w=1.4),
    ]


def move_up():
    return [shape("M8,13.5 V2.8 M3.5,7.3 L8,2.8 L12.5,7.3", stroke=BLUE["dark"], w=1.8)]


def move_down():
    return [shape("M8,2.5 V13.2 M3.5,8.7 L8,13.2 L12.5,8.7", stroke=BLUE["dark"], w=1.8)]


def move_top():
    return [shape("M3,2.5 H13", stroke=SLATE["dark"], w=1.6),
            shape("M8,14 V5.3 M4,9.3 L8,5.3 L12,9.3", stroke=BLUE["dark"], w=1.8)]


def move_bottom():
    return [shape("M3,13.5 H13", stroke=SLATE["dark"], w=1.6),
            shape("M8,2 V10.7 M4,6.7 L8,10.7 L12,6.7", stroke=BLUE["dark"], w=1.8)]


def insert_before():
    """Rows with a "+" on the upper one: insert an item before the selected one."""
    return [
        shape(rect(1.5, 9.5, 14.5, 14.5), fill=SLATE["light"], stroke=SLATE["dark"]),
        shape("M5,2 V8 M2,5 H8", stroke=BLUE["dark"], w=2),
        shape("M10,4.5 H14.5", stroke=SLATE["soft"], w=1.4),
    ]


def auto_width():
    """Column with a double arrow: fit the column widths."""
    return [
        shape("M1.5,2.5 V13.5 M14.5,2.5 V13.5", stroke=SLATE["dark"], w=1.2),
        shape("M4,8 H12 M6.2,5.8 L4,8 L6.2,10.2 M9.8,5.8 L12,8 L9.8,10.2", stroke=BLUE["dark"], w=1.4),
    ]


def tree_box(sign):
    s = [shape(rect(2.5, 2.5, 13.5, 13.5), fill=WHITE, stroke=SLATE["dark"]),
         shape("M5,8 H11", stroke=BLUE["dark"], w=1.6)]
    if sign == "+":
        s.append(shape("M8,5 V11", stroke=BLUE["dark"], w=1.6))
    return s


def save_as():
    return floppy() + [
        shape("M9,15 L9.5,12.6 L14,8.1 L15.9,10 L11.4,14.5 Z", fill=WHITE, stroke=WHITE, w=2),
        shape("M9.5,14.5 L9.9,12.8 L13.9,8.8 L15.2,10.1 L11.2,14.1 Z", fill=AMBER["light"], stroke=AMBER["dark"]),
    ]


# ---------------------------------------------------------------- badges (bottom-right)

def _badge(fill, dark, glyph):
    return [
        shape(circle(11.5, 11.5, 4.6), fill=WHITE),
        shape(circle(11.5, 11.5, 3.8), fill=fill, stroke=dark),
    ] + glyph


def badge_plus():
    return _badge(BLUE["dark"], BLUE["deep"], [shape("M11.5,9.3 V13.7 M9.3,11.5 H13.7", stroke=WHITE, w=1.3)])


def badge_minus():
    return _badge(RED["fill"], RED["dark"], [shape("M9.3,11.5 H13.7", stroke=WHITE, w=1.3)])


def badge_bolt():
    return _badge(AMBER["mid"], AMBER["dark"], [shape("M12.1,8.6 L9.7,12 H11.4 L10.9,14.4 L13.4,10.9 H11.7 Z", fill=WHITE)])


def badge_cross():
    return _badge(RED["fill"], RED["dark"], [shape("M10,10 L13,13 M13,10 L10,13", stroke=WHITE, w=1.3)])


def badge_refresh():
    return _badge(BLUE["dark"], BLUE["deep"], [
        shape("M13.6,11.7 A2.1,2.1 0 1 1 12.4,9.6", stroke=WHITE, w=1.1),
        shape("M11.6,8.4 L13.4,9.5 L11.8,10.8 Z", fill=WHITE),
    ])


def badge_info():
    return _badge(BLUE["dark"], BLUE["deep"], [shape(rect(11, 8.8, 12, 9.8) + " " + rect(11, 10.6, 12, 14), fill=WHITE)])


def badge_play():
    return [
        shape("M8.6,7.6 L15.6,11.6 L8.6,15.6 Z", fill=WHITE, stroke=WHITE, w=1.6),
        shape("M9.5,8.6 L14.6,11.6 L9.5,14.6 Z", fill="#4A7CC0", stroke=BLUE["deep"]),
    ]


def badge_arrow_out():
    return [
        shape("M8.5,11.5 H14.5 M12,9 L14.5,11.5 L12,14", stroke=WHITE, w=3.2),
        shape("M8.5,11.5 H14.5 M12,9 L14.5,11.5 L12,14", stroke=BLUE["deep"], w=1.5),
    ]


def badge_arrow_again():
    return [
        shape("M9,13 H14.5 M12.3,10.8 L14.5,13 L12.3,15.2", stroke=WHITE, w=3.0),
        shape("M9,13 H14.5 M12.3,10.8 L14.5,13 L12.3,15.2", stroke=BLUE["deep"], w=1.4),
    ]


# ---------------------------------------------------------------- the set
# Keys must match SqlExpert.Core/Explorer/ObjectIcons.cs (audited by IconResourceAuditTests).
OBJECTS = {
    "Table": table(),
    "View": eye(),
    "Procedure": gear(),
    "Function": fx(),
    "Trigger": bolt(),
    "Sequence": sequence(),
    "Role": people(),
    "UserDefinedType": type_t(),
    "Schema": schema(),
    "Index": index_icon(),
    "Constraint": padlock(),
    "Synonym": chain(),
    "CustomMessage": warning(),
}

ICONS = {}
ICONS.update(OBJECTS)
ICONS.update({
    # Explorer: registrations / databases
    "DatabaseRegistration": db_registered(),
    "DatabaseRegistrationConnected": db_connected(),
    "Database": db_child(),
    # Main toolbar: database
    "RegisterDatabase": db_registered() + badge_plus(),
    "UnregisterDatabase": db_registered() + badge_minus(),
    "ConnectDatabase": db_registered() + badge_bolt(),
    "DisconnectDatabase": db_connected() + badge_cross(),
    "ReconnectDatabase": db_connected() + badge_refresh(),
    "DatabaseRegistrationInfo": db_registered() + badge_info(),
    "Exit": door_exit(),
    # SQL / scripts
    "SqlEditor": sql_page(),
    "NewSqlEditor": sql_page() + badge_plus(),
    "QueryBuilder": query_builder(),
    "ScriptExecutive": page(colored=False) + badge_play(),
    "SqlMonitor": monitor(),
    "ExtractMetadata": page(colored=False) + badge_arrow_out(),
    "PrintMetadata": printer(),
    "Print": printer(),
    # Security / tools
    "UserManager": person(),
    "GrantManager": key(),
    "ReportManager": report(),
    "BlobViewer": picture(),
    # Edit
    "LoadFromFile": folder_open(),
    "SaveToFile": floppy(),
    "SaveAs": save_as(),
    "Cut": scissors(),
    "Copy": copy_pages(),
    "Paste": clipboard(),
    "Find": magnifier(),
    "SearchAgain": magnifier(6, 6, 4) + badge_arrow_again(),
    "Replace": replace_arrows(),
    # Data navigator / editors
    "First": [shape("M3.5,3 V13", stroke=NAV, w=1.6), tri_left(5.5, 12.5)],
    "Prior": [tri_left(4, 11.5)],
    "Next": [tri_right(4.5, 12)],
    "Last": [tri_right(3.5, 10.5), shape("M12.5,3 V13", stroke=NAV, w=1.6)],
    "InsertRecord": plus_big(),
    "DeleteRecord": minus_big(),
    "EditRecord": pencil(),
    "Post": check(),
    "CancelEdit": cross(),
    "Refresh": refresh(),
    "Execute": play(),
    "Stop": stop(),
    "Compile": bolt(AMBER["mid"], AMBER["dark"]),
    "Commit": check(),
    "Rollback": undo(),
    "FetchAll": fetch_all(),
    # Inner toolbars (Fields / parameters / reorder dialog)
    "Reorder": reorder(),
    "MoveUp": move_up(),
    "MoveDown": move_down(),
    "MoveTop": move_top(),
    "MoveBottom": move_bottom(),
    "InsertBefore": insert_before(),
    "FullText": page(),
    "Restore": undo(),
    "Description": page(lines=True, colored=False),
    "ClearColor": cross(SLATE["dark"]),
    "AutoWidth": auto_width(),
    "ExpandAll": tree_box("+"),
    "CollapseAll": tree_box("-"),
    "Revoke": key() + badge_minus(),
})
# "New <object>" buttons: the object drawing plus the blue "+" badge.
for _k in ("UserDefinedType", "Table", "View", "Procedure", "Trigger", "Sequence", "CustomMessage", "Function", "Role"):
    ICONS["New" + _k] = OBJECTS[_k] + badge_plus()


# ---------------------------------------------------------------- writers

BOUNDS = "M0,0 H16 V16 H0 Z"


def xaml(icons):
    out = [
        "<!--",
        "  SqlExpert icons: original vector drawings on a 16x16 grid (DrawingImage \"Icon.<Name>\").",
        "  GENERATED by tools/icons/generate_icons.py - edit the script, not this file.",
        "  Show them with views:IconImage (greys itself out when disabled).",
        "-->",
        '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
        '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"',
        '                    xmlns:po="http://schemas.microsoft.com/winfx/2006/xaml/presentation/options"',
        '                    xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"',
        '                    mc:Ignorable="po">',
    ]
    for name, shapes in icons.items():
        out.append(f'  <DrawingImage x:Key="Icon.{name}" po:Freeze="True">')
        out.append("    <DrawingImage.Drawing>")
        out.append(f'      <DrawingGroup ClipGeometry="{BOUNDS}">')
        out.append(f'        <GeometryDrawing Brush="Transparent" Geometry="{BOUNDS}"/>')
        for s in shapes:
            brush = f' Brush="{s["fill"]}"' if s["fill"] else ""
            geom = "F1 " + s["d"]
            if s["stroke"]:
                out.append(f'        <GeometryDrawing{brush} Geometry="{geom}">')
                out.append(f'          <GeometryDrawing.Pen><Pen Brush="{s["stroke"]}" Thickness="{n(s["w"])}" '
                           f'StartLineCap="Round" EndLineCap="Round" LineJoin="Round"/></GeometryDrawing.Pen>')
                out.append("        </GeometryDrawing>")
            else:
                out.append(f'        <GeometryDrawing{brush} Geometry="{geom}"/>')
        out.append("      </DrawingGroup>")
        out.append("    </DrawingImage.Drawing>")
        out.append("  </DrawingImage>")
    out.append("</ResourceDictionary>")
    return "\n".join(out) + "\n"


def svg(shapes, size=16):
    body = []
    for s in shapes:
        attrs = [f'd="{s["d"]}"', f'fill="{s["fill"] or "none"}"']
        if s["stroke"]:
            attrs += [f'stroke="{s["stroke"]}"', f'stroke-width="{n(s["w"])}"',
                      'stroke-linecap="round"', 'stroke-linejoin="round"']
        body.append("<path " + " ".join(attrs) + "/>")
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{size}" height="{size}" viewBox="0 0 16 16">'
            + "".join(body) + "</svg>")


def main():
    root = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
    target = os.path.join(root, "SqlExpert.App", "Themes", "Icons.xaml")
    os.makedirs(os.path.dirname(target), exist_ok=True)
    with open(target, "w", encoding="utf-8", newline="\n") as f:
        f.write(xaml(ICONS))
    print(f"{len(ICONS)} icons -> {target}")
    if "--svg" in sys.argv:
        d = sys.argv[sys.argv.index("--svg") + 1]
        os.makedirs(d, exist_ok=True)
        for name, shapes in ICONS.items():
            with open(os.path.join(d, name + ".svg"), "w", encoding="utf-8") as f:
                f.write(svg(shapes))
        print(f"svg -> {d}")


if __name__ == "__main__":
    main()
