"""Renders the card button row (ADR-0020) exactly as VgsEditorScreen draws it, at every zoom.

Why this exists: the offline gate asserts the geometry in numbers (one diameter, one row, one
pitch, the row inside the card, the text span left of it), but it cannot say whether three bare
icons still read as three icons at MinZoom. That is the one claim in ADR-0020 marked
[НЕ ПРОВЕРЕНО], and this picture is what a person compares a screenshot against.

Every number is copied from VgsConstants / PortGeometry, and the icons are the textures the mod
ships — same sources as `controls_reference.py`, which covers the *port* controls.

Run: python tools/icon-atlas/card_row_preview.py
"""

import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEXTURES = os.path.join(ROOT, r'src\VGS.Mod\Textures\VGS\Icons')
OUT = os.path.join(ROOT, r'docs\01-research\images\card-button-row-preview.png')

# ---- VgsConstants (the values the mod compiles) ----
NODE_WIDTH = 0.16
NODE_HEIGHT = 0.036
NODE_STATUS_LINE_HEIGHT = 0.015
CARD_BUTTON_DIAMETER = NODE_HEIGHT * 0.75
CARD_BUTTON_MARGIN = 4.0 / 1080.0
CARD_BUTTON_GAP = 3.0 / 1080.0
CARD_BUTTON_TEXT_GAP = 6.0 / 1080.0
CARD_BUTTON_ICON_RATIO = 0.92
CARD_TEXT_PADDING_RATIO = 0.05
MIN_ZOOM = 0.35
MAX_ZOOM = 3.0

# ---- VgsEditorScreen palette ----
COL_NODE = (38, 45, 58, 255)
COL_NODE_BORDER = (70, 84, 105, 255)
COL_TEXT = (226, 232, 240, 255)
COL_TEXT_DIM = (140, 152, 170, 255)
COL_WATCH = (240, 200, 90, 255)
COL_PLUS_FILL = (244, 247, 252, 255)
COL_CANVAS = (18, 22, 29, 255)

# ---- VgsIconAtlas ----
TEXTURE_SCALE = 1.066667

SCREEN_W = 1920
SCREEN_H = 1080
ASPECT = SCREEN_H / float(SCREEN_W)

ROWS = [
    # (label, card lines, button icons left-to-right)
    ('timer', 2, ['SquarePlus']),
    ('sensor', 2, ['LightbulbSolid', 'LightbulbRegular']),
    ('event controller', 3, ['Bell', 'LightbulbSolid', 'LightbulbRegular']),
]

ZOOMS = [MIN_ZOOM, 0.5, 1.0, 2.0, MAX_ZOOM]


def load(name):
    image = Image.open(os.path.join(TEXTURES, name + '.png'))
    if image.mode != 'RGBA':
        image = image.convert('RGBA')
    return image


TEXTURES_BY_NAME = {name: load(name) for name in
                    ('SquarePlus', 'Bell', 'LightbulbSolid', 'LightbulbRegular')}


def tinted(name, ink_px, colour):
    side = max(1, int(round(ink_px * TEXTURE_SCALE)))
    alpha = TEXTURES_BY_NAME[name].getchannel('A').resize((side, side), Image.BOX)
    out = Image.new('RGBA', (side, side), colour[:3] + (255,))
    out.putalpha(alpha)
    return out


def card_height(lines):
    return NODE_HEIGHT + (NODE_STATUS_LINE_HEIGHT if lines == 3 else 0.0)


def draw_card(canvas, left_px, top_px, zoom, lines, icons):
    """One card: fill, border, the text span, and the right-anchored button row."""
    card_h = card_height(lines)
    w = NODE_WIDTH * zoom * SCREEN_W
    h = card_h * zoom * SCREEN_H

    draw = ImageDraw.Draw(canvas)
    draw.rectangle([left_px, top_px, left_px + w, top_px + h], fill=COL_NODE, outline=COL_NODE_BORDER)

    # The row, right-anchored: the same arithmetic as PortGeometry.TryCardButtonRect.
    count = len(icons)
    diameter_px = CARD_BUTTON_DIAMETER * zoom * SCREEN_H
    half_w_px = (CARD_BUTTON_DIAMETER * 0.5) * ASPECT * zoom * SCREEN_W
    gap_px = CARD_BUTTON_GAP * ASPECT * zoom * SCREEN_W
    stride = half_w_px * 2 + gap_px
    right = left_px + w - CARD_BUTTON_MARGIN * ASPECT * zoom * SCREEN_W

    row_left = right - (count * half_w_px * 2 + (count - 1) * gap_px)

    # Text, clipped to the span left of the row (PortGeometry.TryCardTextSpan).
    band = h / lines
    text_left = left_px + w * CARD_TEXT_PADDING_RATIO
    text_right = row_left - CARD_BUTTON_TEXT_GAP * ASPECT * zoom * SCREEN_W
    draw.rectangle([text_left, top_px + band * 0.2, text_right, top_px + band * 0.8],
                   outline=(90, 105, 130, 255))
    draw.text((text_left + 2, top_px + band * 0.15), 'name', fill=COL_TEXT)
    draw.text((text_left + 2, top_px + band * 1.15), 'subtype', fill=COL_TEXT_DIM)
    if lines == 3:
        draw.text((text_left + 2, top_px + band * 2.15), 'state', fill=COL_WATCH)

    for index, name in enumerate(icons):
        from_right = count - 1 - index
        centre_x = right - half_w_px - from_right * stride
        centre_y = top_px + h * 0.5
        # Every card button is the same near-white; the yellow on the card belongs to the status
        # line and to the watch bars, which mark state rather than a control.
        piece = tinted(name, diameter_px * CARD_BUTTON_ICON_RATIO, COL_PLUS_FILL)
        canvas.alpha_composite(piece, (int(round(centre_x - piece.width / 2.0)),
                                       int(round(centre_y - piece.height / 2.0))))


def main():
    # One column per zoom, the three layouts stacked, each card at its real pixel size. The
    # column width follows the widest card at that zoom rather than a constant: a card at 300%
    # is 921 px wide, and a fixed panel would have cropped it — a preview that hides the right
    # edge of the row is worse than no preview, because the row is the thing being checked.
    pad = 40
    column_widths = [int(round(NODE_WIDTH * zoom * SCREEN_W)) + pad for zoom in ZOOMS]
    panel_h = 260
    total_w = sum(column_widths)
    sheet = Image.new('RGBA', (total_w, panel_h * len(ROWS) + 40), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    column_x = 0
    for column, zoom in enumerate(ZOOMS):
        width = column_widths[column]
        draw.text((column_x + 8, 8), 'zoom %.0f%%' % (zoom * 100),
                  fill=(150, 165, 185, 255))

        for row, (label, lines, icons) in enumerate(ROWS):
            top = 40 + row * panel_h
            card_w = NODE_WIDTH * zoom * SCREEN_W
            left = column_x + (width - card_w) / 2.0
            draw_card(sheet, left, top, zoom, lines, icons)
            draw.text((column_x + 8, top + panel_h - 20), label,
                      fill=(120, 135, 155, 255))

        column_x += width

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert('RGB').save(OUT)
    print('wrote', OUT, sheet.size)


if __name__ == '__main__':
    main()
