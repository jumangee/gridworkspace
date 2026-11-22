"""Renders what the editor's controls should look like, 1:1, at the real zoom levels.

This is the reference picture: every number comes from the same constants the mod compiles, the
textures are the ones the mod ships, and the compositing order matches `VgsEditorScreen`. Its only
purpose is to be compared against a screenshot — "the icon looks wrong" is not actionable, but
"the dot on my screen is a filled square and this shows a ring" is.

Run: python tools/icon-atlas/controls_reference.py
"""

import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEXTURE_DIR = os.path.join(ROOT, r'src\VGS.Mod\Textures\VGS\Icons')
OUT = os.path.join(ROOT, r'tmp\icon-atlas-preview\controls-reference.png')

# --- constants mirrored from VgsConstants ---------------------------------------------------
PORT_BAR_HEIGHT = 0.018
PORT_CONTROL_HEIGHT_RATIO = 0.75
PORT_CONTROL_RADIUS = PORT_BAR_HEIGHT * PORT_CONTROL_HEIGHT_RATIO * 0.5
NODE_HEIGHT = 0.036
PLUS_BUTTON_HEIGHT_RATIO = 0.75
PLUS_BUTTON_RADIUS = NODE_HEIGHT * PLUS_BUTTON_HEIGHT_RATIO * 0.5
PORT_REMOVE_ICON_RATIO = 0.66
PLUS_ICON_RATIO = 0.92
TEXTURE_SCALE = 1.066667

# --- colours mirrored from VgsEditorScreen ---------------------------------------------------
COL_NODE = (38, 45, 58, 255)
COL_PORT_OUT_FILL = (26, 48, 38, 255)
COL_PORT_OUT = (120, 220, 160, 255)
COL_PLUS_FILL = (244, 247, 252, 255)
COL_CANVAS = (18, 22, 29, 255)
COL_BORDER = (70, 84, 104, 255)

SCREEN_HEIGHT = 1080.0
ZOOMS = [0.35, 0.5, 1.0, 2.0, 3.0]


def load(name):
    image = Image.open(os.path.join(TEXTURE_DIR, name + '.png'))
    if image.mode != 'RGBA':
        image = image.convert('RGBA')
    return image


TEXTURES = {name: load(name) for name in ('CircleDot', 'SquarePlus', 'Xmark')}


def icon(name, ink_px, colour):
    """The icon reduced to `ink_px` of ink, tinted — what DrawPixels does on the GPU."""
    side = max(1, int(round(ink_px * TEXTURE_SCALE)))
    alpha = TEXTURES[name].getchannel('A').resize((side, side), Image.BOX)

    out = Image.new('RGBA', (side, side), (0, 0, 0, 0))
    out.putalpha(alpha)
    tinted = Image.new('RGBA', (side, side), colour[:3] + (255,))
    tinted.putalpha(alpha)
    return tinted


def paste_centre(canvas, piece, cx, cy):
    canvas.alpha_composite(piece, (int(round(cx - piece.width / 2)),
                                   int(round(cy - piece.height / 2))))


def draw_port_bar(canvas, left, cy, width, zoom):
    """A port bar with its dot: the bar is drawn first, the dot on top."""
    height = PORT_BAR_HEIGHT * zoom * SCREEN_HEIGHT
    draw = ImageDraw.Draw(canvas)
    draw.rectangle([left, cy - height / 2, left + width, cy + height / 2],
                   fill=COL_PORT_OUT_FILL, outline=COL_BORDER)

    radius = PORT_CONTROL_RADIUS * zoom
    ink = radius * 2 * SCREEN_HEIGHT
    paste_centre(canvas, icon('CircleDot', ink, COL_PORT_OUT), left + height / 2, cy)


def draw_bind_button(canvas, cx, cy, zoom):
    """The bind button: a square-plus icon on the card, no disc behind it."""
    radius = PLUS_BUTTON_RADIUS * zoom
    ink = radius * 2 * SCREEN_HEIGHT * PLUS_ICON_RATIO
    paste_centre(canvas, icon('SquarePlus', ink, COL_PLUS_FILL), cx, cy)


def draw_remove_button(canvas, cx, cy, zoom):
    """The remove button: a bare xmark in the accent colour, no disc (ADR-0019 §5)."""
    radius = PORT_CONTROL_RADIUS * zoom * SCREEN_HEIGHT
    ink = radius * 2 * PORT_REMOVE_ICON_RATIO
    paste_centre(canvas, icon('Xmark', ink, COL_PORT_OUT), cx, cy)


def main():
    os.makedirs(os.path.dirname(OUT), exist_ok=True)

    # One row per zoom, tall enough for the largest control plus a label strip.
    row_height = 260
    canvas = Image.new('RGBA', (1180, row_height * len(ZOOMS) + 40), COL_CANVAS)
    draw = ImageDraw.Draw(canvas)

    for r, zoom in enumerate(ZOOMS):
        top = 20 + r * row_height
        mid = top + row_height // 2

        draw.text((12, top + 4), 'zoom %.0f%%' % (zoom * 100), fill=(150, 165, 185, 255))

        # The card background the bind button sits on.
        card = [120, mid - NODE_HEIGHT * zoom * SCREEN_HEIGHT / 2,
                560, mid + NODE_HEIGHT * zoom * SCREEN_HEIGHT / 2]
        draw.rectangle(card, fill=COL_NODE)

        draw_port_bar(canvas, 130, mid + 70, 420, zoom)
        draw_bind_button(canvas, 340, mid, zoom)
        draw_remove_button(canvas, 480, mid, zoom)

        # Labels under the row.
        draw.text((620, mid - 8), 'port dot / bind / remove', fill=(120, 135, 155, 255))

    canvas.convert('RGB').save(OUT)
    print('wrote', OUT, canvas.size)

    # A magnified strip of the dot alone, so the shape is unambiguous at every zoom.
    strip_w = 200
    strip = Image.new('RGBA', (strip_w * len(ZOOMS), strip_w), COL_CANVAS)
    for i, zoom in enumerate(ZOOMS):
        radius = PORT_CONTROL_RADIUS * zoom * SCREEN_HEIGHT
        ink = radius * 2
        piece = icon('CircleDot', ink, COL_PORT_OUT)
        strip.alpha_composite(piece, (i * strip_w + (strip_w - piece.width) // 2,
                                      (strip_w - piece.height) // 2))
    strip_path = os.path.join(os.path.dirname(OUT), 'dot-at-each-zoom.png')
    strip.convert('RGB').save(strip_path)
    print('wrote', strip_path, strip.size)


if __name__ == '__main__':
    main()
