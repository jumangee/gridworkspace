"""Builds contact sheets from the prototype's alpha masks.

Two pictures, both aimed at a human deciding "does this look right":

  contact-sheet.png     every icon at the pixel sizes the editor really produces, over the
                        editor's own colours, magnified 6x with a 1:1 strip underneath.
  current-vs-icon.png   what the editor draws today (disc, disc + knocked-out plus, "x" text)
                        next to the same control drawn with the icon at the same size.

Nothing here is part of the mod; this is evidence for a decision.
"""

import os
import numpy as np
from PIL import Image, ImageDraw, ImageFont

ROOT = r'e:\Projects\!VGS'
OUT = os.path.join(ROOT, r'tmp\icon-proto\out')
FA = os.path.join(ROOT, r'data\fontawesome-free-7.3.1-desktop')

# Palette copied from VgsEditorScreen.
COL_NODE = (38, 45, 58, 255)
COL_PORT_IN_FILL = (28, 44, 62, 255)
COL_PORT_OUT_FILL = (26, 48, 38, 255)
COL_PORT_IN = (110, 190, 255, 255)
COL_PORT_OUT = (120, 220, 160, 255)
COL_PLUS_FILL = (244, 247, 252, 255)
COL_PLUS_FILL_HOT = (255, 214, 140, 255)
COL_CANVAS = (18, 22, 29, 255)

ZOOM = 6


def load(name, px):
    path = os.path.join(OUT, '%s-%d.pgm' % (name, px))
    return np.array(Image.open(path), dtype=np.uint8)


def tint(alpha, rgb):
    """Straight-alpha composite of a white glyph tinted to rgb over nothing (returns RGBA)."""
    h, w = alpha.shape
    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[:, :, 0] = rgb[0]
    out[:, :, 1] = rgb[1]
    out[:, :, 2] = rgb[2]
    out[:, :, 3] = alpha
    return Image.fromarray(out, 'RGBA')


def disc(radius_px, aa=8):
    """Replicates VgsScreenDraw.Disc: horizontal slabs, one per pixel, no anti-aliasing."""
    size = int(round(radius_px * 2))
    mask = np.zeros((size, size), dtype=np.uint8)
    for y in range(size):
        dy = (y + 0.5) - radius_px
        t = dy / radius_px
        if abs(t) > 1.0:
            continue
        half = radius_px * np.sqrt(max(0.0, 1.0 - t * t))
        x0 = int(round(radius_px - half))
        x1 = int(round(radius_px + half))
        mask[y, max(0, x0):min(size, x1)] = 255
    return mask


def make_contact_sheet():
    # Port control diameter = 0.0135 * 1080 * zoom; plus button = 0.027 * 1080 * zoom.
    port_sizes = [5, 6, 7, 8, 9, 11, 13, 15, 17, 19, 22, 26, 29, 32, 39, 44]
    plus_sizes = [10, 13, 15, 19, 22, 26, 29, 35, 44, 58, 73, 88]

    rows = [
        ('circle-dot-solid', port_sizes, COL_PORT_OUT, COL_PORT_OUT_FILL),
        ('circle-dot-regular', port_sizes, COL_PORT_IN, COL_PORT_IN_FILL),
        ('square-plus-regular', plus_sizes, COL_PLUS_FILL, COL_NODE),
        ('square-plus-solid', plus_sizes, COL_PLUS_FILL, COL_NODE),
        ('xmark-solid', port_sizes, COL_PORT_IN, COL_PORT_IN_FILL),
    ]

    cell_w = 100
    row_h = 120
    margin = 12
    width = margin + cell_w * max(len(r[1]) for r in rows) + margin
    height = margin + row_h * len(rows) + margin

    sheet = Image.new('RGBA', (width, height), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    for r, (name, sizes, ink, background) in enumerate(rows):
        top = margin + r * row_h
        for c, px in enumerate(sizes):
            left = margin + c * cell_w
            cell = Image.new('RGBA', (cell_w, row_h), background)
            alpha = load(name, px)

            # magnified
            big = tint(alpha, ink).resize((px * ZOOM, px * ZOOM), Image.NEAREST)
            cell.alpha_composite(big, ((cell_w - px * ZOOM) // 2, 8))

            # 1:1
            cell.alpha_composite(tint(alpha, ink), ((cell_w - px) // 2, 8 + px * ZOOM + 8))

            sheet.alpha_composite(cell, (left, top))
            draw.text((left + 4, top + row_h - 14), '%dpx' % px, fill=(140, 152, 170, 255))

    sheet = sheet.convert('RGB')
    path = os.path.join(OUT, 'contact-sheet.png')
    sheet.save(path)
    print('wrote', path, sheet.size)


def make_current_vs_icon():
    """Side-by-side: what the editor draws today vs the icon, at the same pixel size."""
    port_sizes = [9, 11, 15, 22, 29]
    plus_sizes = [15, 22, 29, 44, 58]

    cell_w = 150
    row_h = 90
    margin = 12
    rows = 2
    sheet = Image.new('RGBA', (margin * 2 + cell_w * len(port_sizes), margin + row_h * rows),
                      COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    # --- row 0: port dot ---
    for c, px in enumerate(port_sizes):
        left = margin + c * cell_w
        cell = Image.new('RGBA', (cell_w, row_h), COL_PORT_OUT_FILL)
        radius = px / 2.0

        # current: solid disc in the accent colour
        current = tint(disc(radius), COL_PORT_OUT)
        cell.alpha_composite(current, (18, (row_h - px) // 2))

        # icon: the same diameter, circle-dot solid
        icon = tint(load('circle-dot-solid', px), COL_PORT_OUT)
        cell.alpha_composite(icon, (18 + cell_w // 2, (row_h - px) // 2))

        sheet.alpha_composite(cell, (left, margin))
        draw.text((left + 4, margin + row_h - 14), '%dpx' % px, fill=(140, 152, 170, 255))

    # --- row 1: bind button ---
    for c, px in enumerate(plus_sizes):
        left = margin + c * cell_w
        cell = Image.new('RGBA', (cell_w, row_h), COL_NODE)
        radius = px / 2.0
        stroke = px * 0.14
        arm = px * 0.75

        # current: white disc with a plus knocked out in the card colour
        button = tint(disc(radius), COL_PLUS_FILL)
        overlay = Image.new('RGBA', button.size, (0, 0, 0, 0))
        od = ImageDraw.Draw(overlay)
        cx = cy = button.size[0] // 2
        od.rectangle([cx - arm / 2, cy - stroke / 2, cx + arm / 2, cy + stroke / 2],
                     fill=COL_NODE)
        od.rectangle([cx - stroke / 2, cy - arm / 2, cx + stroke / 2, cy + arm / 2],
                     fill=COL_NODE)
        button.alpha_composite(overlay)
        cell.alpha_composite(button, (18, (row_h - px) // 2))

        # icon: square-plus regular, tinted light
        icon = tint(load('square-plus-regular', px), COL_PLUS_FILL)
        cell.alpha_composite(icon, (18 + cell_w // 2, (row_h - px) // 2))

        sheet.alpha_composite(cell, (left, margin + row_h))
        draw.text((left + 4, margin + row_h + row_h - 14), '%dpx' % px,
                  fill=(140, 152, 170, 255))

    sheet = sheet.convert('RGB')
    path = os.path.join(OUT, 'current-vs-icon.png')
    sheet.save(path)
    print('wrote', path, sheet.size)


if __name__ == '__main__':
    make_contact_sheet()
    make_current_vs_icon()
