"""Renders the editor's three controls from the textures the mod actually ships.

This is not the design mock-up: it reads `src/VGS.Mod/Textures/VGS/Icons/*.png`, reduces each
icon to the pixel size the editor computes for a given zoom and screen height, and composites
the control exactly as `VgsEditorScreen` does — the same disc behind the "x", the same colours,
the same size ratios from `VgsConstants`.

It exists because "the icon looks right" is the one claim a gate cannot make. What it can show is
that the texture content is the right shape at the right size, and that the fallback below the
legibility floor is the old geometry rather than a smear.

Run: python tools/icon-atlas/preview.py
"""

import os
import numpy as np
from PIL import Image, ImageDraw

ROOT = r'e:\Projects\!VGS'
TEXTURE_DIR = os.path.join(ROOT, r'src\VGS.Mod\Textures\VGS\Icons')
OUT = os.path.join(ROOT, r'tmp\icon-atlas-preview')

# --- constants mirrored from VgsConstants / VgsEditorScreen -------------------------------
PORT_BAR_HEIGHT = 0.018
PORT_CONTROL_HEIGHT_RATIO = 0.75
PORT_CONTROL_RADIUS = PORT_BAR_HEIGHT * PORT_CONTROL_HEIGHT_RATIO * 0.5
NODE_HEIGHT = 0.036
PLUS_BUTTON_HEIGHT_RATIO = 0.75
PLUS_BUTTON_RADIUS = NODE_HEIGHT * PLUS_BUTTON_HEIGHT_RATIO * 0.5
PORT_REMOVE_ICON_RATIO = 0.66
PLUS_ICON_RATIO = 0.92
# There is no per-icon size floor: the icon is drawn at every zoom (ADR-0019 §6). A floor existed
# and was removed after acceptance in the game, because falling back to the old geometry made the
# control change shape as the player zoomed out. Kept as a named constant so the reason is not
# silently reintroduced by a reader who expects one.
ICON_SIZE_FLOOR = None

# The icons this build ships, in the order the table declares them.
ICON_NAMES = ('CircleDot', 'SquarePlus', 'Xmark')

# Texture side divided by ink height, from the generated table.
TEXTURE_SCALE = 1.066667
MIN_ZOOM = 0.35
MAX_ZOOM = 3.0

COL_NODE = (38, 45, 58, 255)
COL_PORT_OUT_FILL = (26, 48, 38, 255)
COL_PORT_IN_FILL = (28, 44, 62, 255)
COL_PORT_OUT = (120, 220, 160, 255)
COL_PORT_IN = (110, 190, 255, 255)
COL_WATCH = (240, 200, 90, 255)
COL_WATCH_FILL = (62, 52, 24, 255)
COL_PLUS_FILL = (244, 247, 252, 255)
COL_PLUS_FILL_HOT = (255, 214, 140, 255)
COL_CANVAS = (18, 22, 29, 255)

ZOOM = 6


def load_textures():
    """Every icon's alpha, straight from its own file."""
    textures = {}
    for name in ICON_NAMES:
        path = os.path.join(TEXTURE_DIR, name + '.png')
        image = Image.open(path)
        if image.mode != 'RGBA':
            image = image.convert('RGBA')
        textures[name] = np.array(image)[:, :, 3].astype(np.float32)
    return textures


def cell_alpha(textures, name):
    return textures[name]


def resized(alpha, size):
    """The same reduction the GPU performs: box filter down the mip chain."""
    if size <= 0:
        return np.zeros((1, 1), dtype=np.float32)
    source = Image.fromarray(alpha.astype(np.uint8))
    return np.array(source.resize((size, size), Image.BOX), dtype=np.float32)


def tint(alpha, rgb):
    h, w = alpha.shape
    out = np.zeros((h, w, 4), dtype=np.uint8)
    out[:, :, 0] = rgb[0]
    out[:, :, 1] = rgb[1]
    out[:, :, 2] = rgb[2]
    out[:, :, 3] = alpha.astype(np.uint8)
    return Image.fromarray(out, 'RGBA')


def disc(radius):
    """VgsScreenDraw.Disc: one horizontal slab per pixel, no anti-aliasing."""
    size = max(1, int(round(radius * 2)))
    mask = np.zeros((size, size), dtype=np.float32)
    for y in range(size):
        dy = (y + 0.5) - radius
        t = dy / radius
        if abs(t) > 1.0:
            continue
        half = radius * np.sqrt(max(0.0, 1.0 - t * t))
        x0 = int(round(radius - half))
        x1 = int(round(radius + half))
        mask[y, max(0, x0):min(size, x1)] = 255.0
    return mask


def icon_height_px(kind, zoom, screen_height):
    if kind == 'dot':
        return 2 * PORT_CONTROL_RADIUS * zoom * screen_height
    if kind == 'plus':
        return 2 * PLUS_BUTTON_RADIUS * zoom * screen_height * PLUS_ICON_RATIO
    if kind == 'remove':
        return 2 * PORT_CONTROL_RADIUS * zoom * screen_height * PORT_REMOVE_ICON_RATIO
    raise ValueError(kind)


def draw_control(textures, kind, zoom, screen_height):
    """One control as the editor draws it, on the surface it is drawn on.

    There is no size branch: the icon is drawn at every zoom (ADR-0019 §6). The only fallback in
    the mod is a missing texture, which this preview cannot see.
    """
    height = icon_height_px(kind, zoom, screen_height)
    side = int(round(height * TEXTURE_SCALE))

    if kind == 'dot':
        return COL_PORT_OUT_FILL, tint(resized(cell_alpha(textures, 'CircleDot'), side), COL_PORT_OUT)

    if kind == 'plus':
        return COL_NODE, tint(resized(cell_alpha(textures, 'SquarePlus'), side), COL_PLUS_FILL)

    # remove: the bare cross in the accent colour, no disc behind it (ADR-0019 §5)
    return COL_PORT_OUT_FILL, tint(resized(cell_alpha(textures, 'Xmark'), side), COL_PORT_OUT)


def contact_sheet(textures, path):
    screen_height = 1080.0
    zooms = [0.35, 0.5, 0.7, 1.0, 1.4, 2.0, 3.0]

    cell_w = 96
    row_h = 84
    margin = 10
    rows = ['dot', 'plus', 'remove']
    sheet = Image.new('RGBA', (margin * 2 + cell_w * len(zooms), margin + row_h * len(rows)), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    for r, kind in enumerate(rows):
        top = margin + r * row_h
        for c, zoom in enumerate(zooms):
            left = margin + c * cell_w
            background, image = draw_control(textures, kind, zoom, screen_height)
            cell = Image.new('RGBA', (cell_w, row_h), background)

            x = (cell_w - image.size[0]) // 2
            y = (row_h - image.size[1]) // 2
            cell.alpha_composite(image, (max(0, x), max(0, y)))

            sheet.alpha_composite(cell, (left, top))
            draw.text((left + 4, top + row_h - 12), 'x%.2f' % zoom, fill=(140, 152, 170, 255))

    sheet.convert('RGB').save(path)
    print('wrote', path, sheet.size)


def magnified_strip(textures, path):
    """The same three controls at zoom 1, magnified, 1:1 underneath."""
    screen_height = 1080.0
    zoom = 1.0
    cell_w = 120
    row_h = 120
    margin = 10
    kinds = ['dot', 'plus', 'remove']
    sheet = Image.new('RGBA', (margin * 2 + cell_w * len(kinds), margin + row_h), COL_CANVAS)

    for c, kind in enumerate(kinds):
        left = margin + c * cell_w
        background, piece = draw_control(textures, kind, zoom, screen_height)
        cell = Image.new('RGBA', (cell_w, row_h), background)

        big = piece.resize((piece.size[0] * ZOOM, piece.size[1] * ZOOM), Image.NEAREST)
        x = (cell_w - big.size[0]) // 2
        y = (row_h - big.size[1]) // 2
        cell.alpha_composite(big, (max(0, x), max(0, y)))

        sheet.alpha_composite(cell, (left, margin))

    sheet.convert('RGB').save(path)
    print('wrote', path, sheet.size)


def report():
    print('%-8s %6s %8s %8s %8s' % ('control', 'zoom', 'height', 'quad', 'mode'))
    for kind in ('dot', 'plus', 'remove'):
        for zoom in (MIN_ZOOM, 0.5, 1.0, 1.12, 2.0, MAX_ZOOM):
            height = icon_height_px(kind, zoom, 1080.0)
            side = int(round(height * TEXTURE_SCALE))
            # Always the icon. The only fallback is a missing texture, which this table cannot
            # see; the 'geometry' mode used to appear here for small sizes and no longer exists.
            mode = 'icon' if side >= 1 else 'degenerate'
            print('%-8s %6.2f %8.1f %8d %8s' % (kind, zoom, height, side, mode))


def main():
    os.makedirs(OUT, exist_ok=True)
    textures = load_textures()
    for name, alpha in textures.items():
        print('%s %dx%d' % (name, alpha.shape[1], alpha.shape[0]))
    report()
    contact_sheet(textures, os.path.join(OUT, 'controls.png'))
    magnified_strip(textures, os.path.join(OUT, 'controls-zoom1.png'))


if __name__ == '__main__':
    main()
