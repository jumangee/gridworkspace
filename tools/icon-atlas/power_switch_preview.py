"""Renders the card power switch (ADR-0035) exactly as VgsEditorScreen draws it.

Why this exists: the offline gate asserts the arithmetic — the switch fills the upper 40% of the
card's height, its width is the icon's own ink aspect times that height, it sits at the card's
top-left corner, the block's name starts to its right, and the hit target is the ink plus a
padding clipped to the card. None of that answers the two questions only an eye can answer:

  * is the switch *actually* wider than a button of the card's right-hand row, which is what the
    product owner asked to verify — and by how much;
  * do the three colours (green on, grey off, dark red unavailable) still read as three states at
    MinZoom, next to a damage band that is itself red.

So this picture is the thing a person compares a screenshot against. Every number is copied from
VgsConstants / PortGeometry / VgsPalette, and the icons are the textures the mod ships — same
sources as `card_row_preview.py` and `damage_band_preview.py`.

Run: python tools/icon-atlas/power_switch_preview.py
"""

import os

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEXTURES = os.path.join(ROOT, r'src\GridWorkspace.Mod\Textures\VGS\Icons')
OUT = os.path.join(ROOT, r'docs\01-research\images\power-switch-preview.png')

# ---- VgsConstants / PortGeometry (the values the mod compiles) ----
NODE_WIDTH = 0.16
NODE_HEIGHT = 0.036
NODE_STATUS_LINE_HEIGHT = 0.015
CARD_BUTTON_DIAMETER = NODE_HEIGHT * 0.60      # ADR-0021
CARD_BUTTON_MARGIN = 4.0 / 1080.0
CARD_BUTTON_GAP = 3.0 / 1080.0
CARD_BUTTON_TEXT_GAP = 6.0 / 1080.0
CARD_BUTTON_ICON_RATIO = 0.92
CARD_TEXT_PADDING_RATIO = 0.05
POWER_SWITCH_BAND_RATIO = 0.40                 # ADR-0035: of the column's inner height
POWER_SWITCH_PADDING = 2.0 / 1080.0            # ADR-0035: PowerSwitchPadding, vertical units
MIN_ZOOM = 0.35
MAX_ZOOM = 3.0

# ---- VgsIconAtlas: the generated numbers for the two toggle icons ----
TOGGLE_INK_ASPECT = 1.5                        # ink 576 x 384
TOGGLE_TEXTURE_SCALE = 1.6                     # 256 px texture / 160 px of ink height
ICON_TEXTURE_SCALE = 1.066667                  # every square icon

# ---- VgsPalette ----
COL_NODE = (38, 45, 58, 255)
COL_NODE_BORDER = (70, 84, 105, 255)
COL_TEXT = (226, 232, 240, 255)
COL_TEXT_DIM = (140, 152, 170, 255)
COL_WATCH = (240, 200, 90, 255)
COL_PLUS_FILL = (244, 247, 252, 255)
COL_CANVAS = (18, 22, 29, 255)
COL_DAMAGE = (200, 60, 55, 128)
COL_POWER_ON = (110, 215, 130, 255)
COL_POWER_OFF = (119, 119, 119, 255)
COL_POWER_UNAVAILABLE = (119, 0, 0, 255)

SCREEN_W = 1920
SCREEN_H = 1080
ASPECT = SCREEN_H / float(SCREEN_W)

# One row per state; `buttons` is the row of icons to the right, `integrity` drives the damage band.
ROWS = [
    ('on · timer', 'ToggleOn', COL_POWER_ON, ['SquarePlus'], 1.00),
    ('off · timer', 'ToggleOff', COL_POWER_OFF, ['SquarePlus'], 1.00),
    ('unavailable · timer', 'ToggleOff', COL_POWER_UNAVAILABLE, ['SquarePlus'], 0.35),
    ('on · controller, 4 buttons', 'ToggleOn', COL_POWER_ON,
     ['Bell', 'LightbulbSolid', 'LightbulbRegular', 'ShareFromSquare'], 1.00),
]

ZOOMS = [MIN_ZOOM, 0.5, 1.0, 2.0, MAX_ZOOM]


def load(name):
    image = Image.open(os.path.join(TEXTURES, name + '.png'))
    if image.mode != 'RGBA':
        image = image.convert('RGBA')
    return image


TEXTURES_BY_NAME = {name: load(name) for name in
                    ('ToggleOn', 'ToggleOff', 'SquarePlus', 'Bell', 'LightbulbSolid',
                     'LightbulbRegular', 'ShareFromSquare')}


def tinted(name, ink_h_px, colour):
    """The ink at a given ink height, tinted — the same contract as VgsScreenDraw.Icon."""
    scale = TOGGLE_TEXTURE_SCALE if name in ('ToggleOn', 'ToggleOff') else ICON_TEXTURE_SCALE
    side = max(1, int(round(ink_h_px * scale)))
    alpha = TEXTURES_BY_NAME[name].getchannel('A').resize((side, side), Image.BOX)
    out = Image.new('RGBA', (side, side), colour[:3] + (255,))
    out.putalpha(alpha)
    return out


def card_height(lines):
    return NODE_HEIGHT + (NODE_STATUS_LINE_HEIGHT if lines == 3 else 0.0)


def draw_card(canvas, left_px, top_px, zoom, icons, switch_icon, switch_colour, integrity):
    card_h = NODE_HEIGHT + (NODE_STATUS_LINE_HEIGHT if len(icons) == 4 else 0.0)
    w = NODE_WIDTH * zoom * SCREEN_W
    h = card_h * zoom * SCREEN_H

    draw = ImageDraw.Draw(canvas)
    draw.rectangle([left_px, top_px, left_px + w, top_px + h], fill=COL_NODE,
                   outline=COL_NODE_BORDER)

    # The damage band, under everything (ADR-0028).
    if integrity < 1.0:
        band = (1.0 - integrity) * w
        band_layer = Image.new('RGBA', (max(1, int(band)), max(1, int(h))), COL_DAMAGE)
        canvas.alpha_composite(band_layer, (int(left_px + w - band), int(top_px)))

    # ---- the switch: the upper band of the state column, inset by the padding ----
    # The column's inner box is the card less the padding above and below; the band is a fraction
    # of that box, not of the card (ADR-0035 §1).
    switch_pad = POWER_SWITCH_PADDING * zoom * SCREEN_H
    switch_inner = h - 2.0 * switch_pad
    switch_h = switch_inner * POWER_SWITCH_BAND_RATIO
    switch_w = switch_h * TOGGLE_INK_ASPECT
    switch_x = left_px + CARD_BUTTON_MARGIN * ASPECT * zoom * SCREEN_W
    piece = tinted(switch_icon, switch_h, switch_colour)
    canvas.alpha_composite(piece, (int(round(switch_x + switch_w / 2.0 - piece.width / 2.0)),
                                   int(round(top_px + switch_pad + switch_h / 2.0 - piece.height / 2.0))))

    # ---- the row, right-anchored: the same arithmetic as PortGeometry.CardButtonRectAt ----
    count = len(icons)
    diameter_px = CARD_BUTTON_DIAMETER * zoom * SCREEN_H
    half_w_px = (CARD_BUTTON_DIAMETER * 0.5) * ASPECT * zoom * SCREEN_W
    gap_px = CARD_BUTTON_GAP * ASPECT * zoom * SCREEN_W
    stride = half_w_px * 2 + gap_px
    right = left_px + w - CARD_BUTTON_MARGIN * ASPECT * zoom * SCREEN_W
    row_left = right - (count * half_w_px * 2 + (count - 1) * gap_px)

    # ---- text, between the switch's column and the row ----
    band = h / (3 if len(icons) == 4 else 2)
    text_left = switch_x + switch_w + CARD_BUTTON_TEXT_GAP * ASPECT * zoom * SCREEN_W
    text_right = row_left - CARD_BUTTON_TEXT_GAP * ASPECT * zoom * SCREEN_W
    if text_right > text_left:
        draw.rectangle([text_left, top_px + band * 0.2, text_right, top_px + band * 0.8],
                       outline=(90, 105, 130, 255))
        draw.text((text_left + 2, top_px + band * 0.15), 'name', fill=COL_TEXT)
        draw.text((text_left + 2, top_px + band * 1.15), 'subtype', fill=COL_TEXT_DIM)
        if len(icons) == 4:
            draw.text((text_left + 2, top_px + band * 2.15), 'state', fill=COL_WATCH)

    for index, name in enumerate(icons):
        from_right = count - 1 - index
        centre_x = right - half_w_px - from_right * stride
        centre_y = top_px + h * 0.5
        icon = tinted(name, diameter_px * CARD_BUTTON_ICON_RATIO, COL_PLUS_FILL)
        canvas.alpha_composite(icon, (int(round(centre_x - icon.width / 2.0)),
                                      int(round(centre_y - icon.height / 2.0))))
    return switch_w, switch_h, diameter_px


def main():
    pad = 46
    column_widths = [int(round(NODE_WIDTH * zoom * SCREEN_W)) + pad for zoom in ZOOMS]
    panel_h = 150
    total_w = sum(column_widths)
    sheet = Image.new('RGBA', (total_w, panel_h * len(ROWS) + 46), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    column_x = 0
    measured = None
    for column, zoom in enumerate(ZOOMS):
        width = column_widths[column]
        draw.text((column_x + 8, 8), 'zoom %.0f%%' % (zoom * 100), fill=(150, 165, 185, 255))

        for row, (label, switch_icon, colour, icons, integrity) in enumerate(ROWS):
            top = 46 + row * panel_h
            card_w = NODE_WIDTH * zoom * SCREEN_W
            left = column_x + (width - card_w) / 2.0
            sizes = draw_card(sheet, left, top, zoom, icons, switch_icon, colour, integrity)
            # Measured on the standard-height card, at 100%: that is the pair the requirement
            # compares, and a controller's taller card would make the numbers depend on which row
            # happened to be drawn last.
            if abs(zoom - 1.0) < 1e-6 and len(icons) == 1:
                measured = sizes
            draw.text((column_x + 8, top + panel_h - 22), label, fill=(120, 135, 155, 255))

        column_x += width

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert('RGB').save(OUT)

    switch_w, switch_h, diameter = measured
    print('wrote', OUT, sheet.size)
    print('at 100%%: switch %.1f x %.1f px, row button ink %.1f px'
          % (switch_w, switch_h, diameter * CARD_BUTTON_ICON_RATIO))
    print('switch is %.0f%% wider than a row button\'s ink, and %.0f%% shorter'
          % (100.0 * (switch_w / (diameter * CARD_BUTTON_ICON_RATIO) - 1.0),
             100.0 * (1.0 - switch_h / (diameter * CARD_BUTTON_ICON_RATIO))))


if __name__ == '__main__':
    main()
