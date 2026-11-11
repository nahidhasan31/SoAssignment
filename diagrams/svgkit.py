#!/usr/bin/env python3
"""Drawing kit for this submission's figures.

The subject is an engineered platform, so the figures are drawn as engineering
drawings rather than as slides: the line does the work. Its weight says how
important the path is, its pattern says what travels along it, and its label sits
on the line at the midpoint. Boxes carry no fill unless MaltaShop does not run
the thing inside them, and each figure ends in a title block.

Everything is drawn at a design width of 880 units.
"""
from pathlib import Path

OUT = Path(__file__).parent / "src"

TITLE_FONT = "Rockwell, Georgia, serif"      # the one slab: titles only
FONT = "Gill Sans, Gill Sans MT, sans-serif"  # everything else

INK = "#1b2a3a"
RULE = "#7d8c9c"
QUIET = "#5f7183"
SIGNAL = "#b4820e"      # the broker and the messages on it. Nothing else.
FAULT = "#8c2f22"       # the compensating branch. Nothing else.
WASH = "#eceff3"        # a thing MaltaShop does not run
WHITE = "#ffffff"

COLOURS = {"ink": INK, "rule": RULE, "quiet": QUIET, "signal": SIGNAL, "fault": FAULT}

# The line grammar. Declared once so that a figure cannot quietly invent a fifth.
HEAVY, LIGHT, HAIR = 2.6, 1.2, 0.8
ASYNC = "6 4"
OBSERVE = "1.5 3"


def svg(width, height, body):
    markers = "".join(
        f'<marker id="m-{name}" viewBox="0 0 10 10" refX="9" refY="5" markerWidth="6" '
        f'markerHeight="6" orient="auto-start-reverse">'
        f'<path d="M0,1.5 L9,5 L0,8.5 z" fill="{colour}"/></marker>'
        for name, colour in COLOURS.items())
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="{height}" '
            f'viewBox="0 0 {width} {height}" font-family="{FONT}">\n<defs>{markers}</defs>\n'
            f'<rect width="{width}" height="{height}" fill="white"/>\n{body}\n</svg>\n')


def esc(value):
    return str(value).replace("&", "&amp;").replace("<", "&lt;").replace(">", "&gt;")


def text(x, y, content, size=12, anchor="middle", colour=INK, weight=None,
         italic=False, slab=False):
    style = f' font-weight="{weight}"' if weight else ""
    style += ' font-style="italic"' if italic else ""
    family = f' font-family="{TITLE_FONT}"' if slab else ""
    return (f'<text x="{x}" y="{y}" font-size="{size}" text-anchor="{anchor}" fill="{colour}"'
            f'{family}{style}>{esc(content)}</text>')


def lines(x, y, rows, size=11, gap=15, **kw):
    return "\n".join(text(x, y + i * gap, row, size=size, **kw) for i, row in enumerate(rows))


def box(x, y, w, h, stroke=INK, width=LIGHT, fill=WHITE, dashed=False):
    dash = f' stroke-dasharray="{ASYNC}"' if dashed else ""
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" stroke="{stroke}" '
            f'stroke-width="{width}"{dash}/>')


def unit(x, y, w, h, name, rows=(), stroke=INK, width=LIGHT, fill=WHITE, size=12.5, gap=14):
    """One item on the drawing. A fill means MaltaShop does not run it."""
    parts = [box(x, y, w, h, stroke, width, fill),
             text(x + w / 2, y + 19, name, size=size, weight="600", colour=stroke, slab=True)]
    if rows:
        parts.append(lines(x + w / 2, y + 36, rows, size=10.5, gap=gap, colour=QUIET))
    return "\n".join(parts)


def line(x1, y1, x2, y2, colour="ink", width=LIGHT, pattern=None, arrowhead=True, both=False):
    dash = f' stroke-dasharray="{pattern}"' if pattern else ""
    head = f' marker-end="url(#m-{colour})"' if arrowhead else ""
    start = f' marker-start="url(#m-{colour})"' if both else ""
    return (f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{COLOURS[colour]}" '
            f'stroke-width="{width}"{dash}{head}{start}/>')


def run(points, colour="ink", width=LIGHT, pattern=None, arrowhead=True):
    dash = f' stroke-dasharray="{pattern}"' if pattern else ""
    head = f' marker-end="url(#m-{colour})"' if arrowhead else ""
    pts = " ".join(f"{x},{y}" for x, y in points)
    return (f'<polyline points="{pts}" fill="none" stroke="{COLOURS[colour]}" '
            f'stroke-width="{width}"{dash}{head}/>')


def on_line(x1, y1, x2, y2, label, colour=QUIET, size=9.5, offset=-6):
    """A label sits on its line at the midpoint, as on a schematic."""
    return text((x1 + x2) / 2, (y1 + y2) / 2 + offset, label, size=size, colour=colour)


def rule_h(x1, y, x2, colour=RULE, width=HAIR):
    return f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="{colour}" stroke-width="{width}"/>'


def rule_v(x, y1, y2, colour=RULE, width=HAIR):
    return f'<line x1="{x}" y1="{y1}" x2="{x}" y2="{y2}" stroke="{colour}" stroke-width="{width}"/>'


def store(x, y, label, colour=INK):
    return "\n".join([
        f'<path d="M{x},{y + 3} v6 a4.5,3 0 0 0 9,0 v-6" fill="none" stroke="{colour}" stroke-width="0.9"/>',
        f'<ellipse cx="{x + 4.5}" cy="{y + 3}" rx="4.5" ry="3" fill="none" stroke="{colour}" stroke-width="0.9"/>',
        text(x + 13, y + 10, label, size=10, anchor="start", colour=QUIET)])


def title_block(x, y, w, figure, subject, revision):
    """The drawing's own record: what it shows, and which revision it is. Every
    engineering drawing carries one, and it holds information rather than decorating."""
    h = 38
    cells = [(0.16, figure), (0.62, subject), (0.22, revision)]
    parts = [box(x, y, w, h, INK, LIGHT, WHITE)]
    cursor = x
    for i, (share, value) in enumerate(cells):
        cw = w * share
        if i:
            parts.append(rule_v(cursor, y, y + h, INK, LIGHT))
        parts.append(text(cursor + 8, y + 14, ("figure", "shows", "revision")[i],
                          size=8.5, anchor="start", colour=QUIET))
        parts.append(text(cursor + 8, y + 29, value, size=11, anchor="start", colour=INK))
        cursor += cw
    return "\n".join(parts)


def legend(x, y, entries):
    """What each line weight and pattern means, stated once per figure."""
    parts = []
    for i, (width, pattern, colour, label) in enumerate(entries):
        yy = y + i * 17
        parts.append(line(x, yy, x + 34, yy, colour, width, pattern, arrowhead=False))
        parts.append(text(x + 42, yy + 4, label, size=10, anchor="start", colour=QUIET))
    return "\n".join(parts)
