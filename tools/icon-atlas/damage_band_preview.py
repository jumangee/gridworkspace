"""Renders the block damage band (ADR-0028) exactly as VgsEditorScreen draws it.

Why this exists: the offline gate asserts the rule in numbers (width = 1 - integrity, right
anchored, full card height, no band when undamaged, no band on a group), but it cannot say
whether a half-opacity red still lets the label and the button row read on top of it. That is
the one claim in ADR-0028 that only an eye can check, and this picture is what a person
compares a screenshot against.

Every number is copied from VgsConstants / PortGeometry / the screen's palette, and the icons
are the textures the mod ships — same sources as `card_row_preview.py`.

Run: python tools/icon-atlas/damage_band_preview.py
"""

import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEXTURES = os.path.join(ROOT, r'src\VGS.Mod\Textures\VGS\Icons')
OUT = os.path.join(ROOT, r'docs\01-research\images\damage-band-preview.png')

# ---- VgsConstants / PortGeometry (the values the mod compiles) ----
NODE_WIDTH = 0.16
NODE_HEIGHT = 0.036
NODE_STATUS_LINE_HEIGHT = 0.015
CARD_BUTTON_DIAMETER = NODE_HEIGHT * 0.60      # ADR-0021 reduced it from 0.75
CARD_BUTTON_MARGIN = 4.0 / 1080.0
CARD_BUTTON_GAP = 3.0 / 1080.0
CARD_BUTTON_TEXT_GAP = 6.0 / 1080.0
CARD_BUTTON_ICON_RATIO = 0.92
CARD_TEXT_PADDING_RATIO = 0.05
INTEGRITY_TOLERANCE = 0.001                    # BlockState.IntegrityTolerance
MIN_ZOOM = 0.35
MAX_ZOOM = 3.0

# ---- VgsEditorScreen palette ----
COL_NODE = (38, 45, 58, 255)
COL_NODE_BORDER = (70, 84, 105, 255)
COL_NODE_SELECTED = (255, 170, 60, 255)
COL_TEXT = (226, 232, 240, 255)
COL_TEXT_DIM = (140, 152, 170, 255)
COL_WATCH = (240, 200, 90, 255)
COL_PLUS_FILL = (244, 247, 252, 255)
COL_CANVAS = (18, 22, 29, 255)
COL_DAMAGE = (200, 60, 55, 128)                # 50% opacity

# ---- VgsIconAtlas ----
TEXTURE_SCALE = 1.066667

SCREEN_W = 1920
SCREEN_H = 1080
ASPECT = SCREEN_H / float(SCREEN_W)

# One row per integrity value; None means "a group", which never has a band.
ROWS = [
    ('100% — clean, no band', 1.00, False),
    ('70% — right 30%', 0.70, False),
    ('50% — right half', 0.50, False),
    ('10% — almost all', 0.10, False),
    ('0% — destroyed', 0.00, False),
    ('70% — selected (border on top)', 0.70, True),
]

ZOOMS = [MIN_ZOOM, 1.0, 2.0]


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


def damage_width(integrity):
    """PortGeometry.TryDamageRect: width is the *damage*, and nothing at all when undamaged."""
    if integrity >= 1.0 - INTEGRITY_TOLERANCE:
        return 0.0
    damage = 1.0 - integrity
    return NODE_WIDTH * min(1.0, max(0.0, damage))


def fit_band(width, card_left, card_width, min_width):
    """PortGeometry.FitDamageBand: floor the width, clamp it to the card, re-anchor to the right.

    The first version of the mod's drawing code compared a normalized width against 1f — the
    whole screen — so every damaged card painted a band a full screen wide. The rule now lives
    in one place in graph units, and this function is that rule, so the preview cannot show a
    band the mod would not draw.
    """
    if width <= 0.0 or card_width <= 0.0:
        return 0.0, 0.0

    if min_width > 0.0 and width < min_width:
        width = min_width
    if width > card_width:
        width = card_width

    return card_left + card_width - width, width


def draw_card(canvas, left_px, top_px, zoom, lines, icons, integrity, selected):
    """One card in the mod's own order: fill → damage → border → text → buttons."""
    card_h = card_height(lines)
    w = NODE_WIDTH * zoom * SCREEN_W
    h = card_h * zoom * SCREEN_H
    right = left_px + w

    draw = ImageDraw.Draw(canvas)

    # 1. The card's own fill.
    draw.rectangle([left_px, top_px, right, top_px + h], fill=COL_NODE)

    # 2. The damage band, composited over the fill: a separate layer, so its 50% alpha mixes
    #    with the card and not with the canvas — exactly as the sprite does in the game. The
    #    one-pixel floor goes through the same FitDamageBand rule the mod uses.
    card_w_graph = NODE_WIDTH
    one_pixel_graph = 1.0 / (SCREEN_W * zoom)
    band_x, band_w = fit_band(damage_width(integrity), 0.0, card_w_graph, one_pixel_graph)
    if band_w > 0.0:
        left_px_band = left_px + band_x * zoom * SCREEN_W
        band_px = band_w * zoom * SCREEN_W
        layer = Image.new('RGBA', canvas.size, (0, 0, 0, 0))
        ImageDraw.Draw(layer).rectangle(
            [left_px_band, top_px, left_px_band + band_px, top_px + h], fill=COL_DAMAGE)
        canvas.alpha_composite(layer)

    # 3. The border: selection and hover live here, so the band never covers them.
    draw.rectangle([left_px, top_px, right, top_px + h],
                   outline=COL_NODE_SELECTED if selected else COL_NODE_BORDER,
                   width=2 if selected else 1)

    # 4. Text, clipped to the span left of the row (PortGeometry.TryCardTextSpan).
    count = len(icons)
    diameter_px = CARD_BUTTON_DIAMETER * zoom * SCREEN_H
    half_w_px = (CARD_BUTTON_DIAMETER * 0.5) * ASPECT * zoom * SCREEN_W
    gap_px = CARD_BUTTON_GAP * ASPECT * zoom * SCREEN_W
    stride = half_w_px * 2 + gap_px
    row_right = right - CARD_BUTTON_MARGIN * ASPECT * zoom * SCREEN_W
    row_left = row_right - (count * half_w_px * 2 + (count - 1) * gap_px)

    band = h / lines
    text_left = left_px + w * CARD_TEXT_PADDING_RATIO
    text_right = row_left - CARD_BUTTON_TEXT_GAP * ASPECT * zoom * SCREEN_W
    if text_right > text_left:
        draw.text((text_left + 2, top_px + band * 0.15), 'name', fill=COL_TEXT)
        draw.text((text_left + 2, top_px + band * 1.15), 'subtype', fill=COL_TEXT_DIM)
        if lines == 3:
            draw.text((text_left + 2, top_px + band * 2.15), 'state', fill=COL_WATCH)

    # 5. The button row, right-anchored, on top of everything.
    for index, name in enumerate(icons):
        from_right = count - 1 - index
        centre_x = row_right - half_w_px - from_right * stride
        centre_y = top_px + h * 0.5
        piece = tinted(name, diameter_px * CARD_BUTTON_ICON_RATIO, COL_PLUS_FILL)
        canvas.alpha_composite(piece, (int(round(centre_x - piece.width / 2.0)),
                                       int(round(centre_y - piece.height / 2.0))))


def main():
    pad = 40
    column_widths = [int(round(NODE_WIDTH * zoom * SCREEN_W)) + pad for zoom in ZOOMS]
    panel_h = 130
    # The label sits *above* its card, not in the gap under it: a caption in the middle of a gap
    # reads as belonging to the next card, and this sheet exists to be compared by eye.
    label_h = 18
    total_w = sum(column_widths)
    sheet = Image.new('RGBA', (total_w, panel_h * len(ROWS) + 40), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    column_x = 0
    for column, zoom in enumerate(ZOOMS):
        width = column_widths[column]
        draw.text((column_x + 8, 8), 'zoom %.0f%%' % (zoom * 100), fill=(150, 165, 185, 255))

        for row, (label, integrity, selected) in enumerate(ROWS):
            top = 40 + row * panel_h
            card_w = NODE_WIDTH * zoom * SCREEN_W
            left = column_x + (width - card_w) / 2.0
            draw.text((column_x + 8, top), label, fill=(120, 135, 155, 255))
            # A timer-shaped card: two lines, one bind button.
            draw_card(sheet, left, top + label_h, zoom, 2, ['SquarePlus'], integrity, selected)

        column_x += width

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert('RGB').save(OUT)
    print('wrote', OUT, sheet.size)


if __name__ == '__main__':
    main()
