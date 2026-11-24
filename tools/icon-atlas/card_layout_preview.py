"""Computes and renders the card layout (ADR-0020/0028/0035) as one labelled drawing.

Why this exists: the card's geometry is spread over VgsConstants (the numbers), PortGeometry
(the arithmetic) and four ADRs (the rules), and every one of them can be right while the picture
is wrong. The offline gate asserts the numbers; this script *draws* them from the same constants
and prints the same values it drew, so a reader can check a screenshot against one picture
instead of reconstructing the card from five files.

It is also the tool that answers "what does changing this constant do?" — run it after editing
VgsConstants and the picture and the printed table move together.

**The constants are parsed out of `VgsConstants.cs`, not copied.** A copy would be a second
source of truth that silently disagrees after the first edit, and a picture that is wrong about
the layout is worse than no picture. Only the values that live in *other* files are written here
— the palette, and the atlas numbers — and each says which file it came from.

Run: python tools/icon-atlas/card_layout_preview.py
"""

import ast
import os
import re

from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEXTURES = os.path.join(ROOT, r'src\GridWorkspace.Mod\Textures\VGS\Icons')
CONSTANTS_CS = os.path.join(ROOT, r'src\GridWorkspace.Mod\Data\Scripts\VGS\Core\VgsConstants.cs')
OUT = os.path.join(ROOT, r'docs\01-research\images\card-layout.png')


# --------------------------------------------------------------------------- constants
# Parsed from the mod. `const float X = <expr>;` where <expr> is a literal or a product of
# previously defined names — which is exactly how VgsConstants is written, and is why this can be
# parsed rather than evaluated as C#.
def load_constants(path):
    """Parses `public const float X = <arithmetic>;` into a name → value map.

    The expressions in VgsConstants are sums, products and quotients of literals and of earlier
    constants (`PortControlMarginPixels / ReferenceHeightPixels`), so they are evaluated with a
    tiny AST walk that accepts only numbers and the four operators. `eval` is not used: this file
    is a build tool, and "the expression can only be arithmetic" is cheaper to enforce than to
    assume.
    """
    values = {}
    pattern = re.compile(r'public\s+const\s+(?:float|int)\s+(\w+)\s*=\s*([^;]+);')

    def evaluate(expression):
        expression = expression.replace('f', '').replace('F', '')
        tree = ast.parse(expression, mode='eval')

        def walk(node):
            if isinstance(node, ast.Expression):
                return walk(node.body)
            if isinstance(node, ast.Constant):
                return float(node.value)
            if isinstance(node, ast.Name):
                if node.id not in values:
                    raise KeyError(node.id)
                return values[node.id]
            if isinstance(node, ast.BinOp):
                left, right = walk(node.left), walk(node.right)
                if isinstance(node.op, ast.Add):
                    return left + right
                if isinstance(node.op, ast.Sub):
                    return left - right
                if isinstance(node.op, ast.Mult):
                    return left * right
                if isinstance(node.op, ast.Div):
                    return left / right
            raise ValueError(ast.dump(node))

        return walk(tree)

    with open(path, 'r', encoding='utf-8') as handle:
        for line in handle:
            match = pattern.search(line)
            if not match:
                continue
            name, expression = match.group(1), match.group(2).strip()
            try:
                values[name] = evaluate(expression)
            except KeyError:
                # A constant defined in terms of one from another file — `CardButtonDiameter`
                # is not, but a future one might be. Skipping it is honest: the caller below
                # names the constant it needs, so a skip shows up as a KeyError with the name.
                continue
    return values


C = load_constants(CONSTANTS_CS)

NODE_WIDTH = C['NodeWidth']
NODE_HEIGHT = C['NodeHeight']
NODE_STATUS_LINE_HEIGHT = C['NodeStatusLineHeight']
CARD_TEXT_PADDING_RATIO = C['CardTextPaddingRatio'] if 'CardTextPaddingRatio' in C else None
CARD_BUTTON_HEIGHT_RATIO = C['CardButtonHeightRatio']
CARD_BUTTON_DIAMETER = C['CardButtonDiameter']
CARD_BUTTON_ICON_RATIO = C['CardButtonIconRatio']
CARD_BUTTON_MARGIN_PX = C['CardButtonMarginPixels']
CARD_BUTTON_GAP_PX = C['CardButtonGapPixels']
CARD_BUTTON_TEXT_GAP_PX = C['CardButtonTextGapPixels']
POWER_SWITCH_BAND_RATIO = C['PowerSwitchBandRatio']
POWER_SWITCH_PADDING_PX = C['PowerSwitchPaddingPixels']
POWER_SWITCH_HIT_PADDING_PX = C['PowerSwitchHitPaddingPixels']
PORT_BAR_HEIGHT = C['PortBarHeight']
PORT_CONTROL_HEIGHT_RATIO = C['PortControlHeightRatio']
PORT_CONTROL_MARGIN_PX = C['PortControlMarginPixels']
PORT_LABEL_PADDING = C['PortLabelPadding']
REFERENCE_HEIGHT_PX = C['ReferenceHeightPixels']
MIN_ZOOM = C['MinZoom']
MAX_ZOOM = C['MaxZoom']

# `CardTextPaddingRatio` is a *const on PortGeometry*, not on VgsConstants (it is a fraction of
# the card's width and belongs with the geometry). Read from its own file so the two stay in step.
PORTGEOMETRY_CS = os.path.join(ROOT, r'src\GridWorkspace.Mod\Data\Scripts\VGS\Ui\PortGeometry.cs')
if CARD_TEXT_PADDING_RATIO is None:
    with open(PORTGEOMETRY_CS, 'r', encoding='utf-8') as handle:
        for line in handle:
            match = re.search(r'public\s+const\s+float\s+CardTextPaddingRatio\s*=\s*([0-9.]+)f', line)
            if match:
                CARD_TEXT_PADDING_RATIO = float(match.group(1))
                break
    if CARD_TEXT_PADDING_RATIO is None:
        raise SystemExit('CardTextPaddingRatio not found in PortGeometry.cs')

# ---- VgsIconAtlas.g.cs: generated by build-icon-atlas.ps1 ----
# Not parsed: these are two values of a generated table with ten entries, and a regex over a
# switch statement would be more fragile than the two numbers are worth.
TOGGLE_INK_ASPECT = 1.5          # ToggleOn / ToggleOff ink 576 x 384
TOGGLE_TEXTURE_SCALE = 1.6
ICON_TEXTURE_SCALE = 1.066667

# ---- VgsPalette.cs ----
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

# The window the numbers are quoted for. 16:9 is what the editor is normally used in, and the
# aspect is what turns the layout's vertical units into horizontal ones.
SCREEN_W = 1920.0
SCREEN_H = 1080.0
ASPECT = SCREEN_H / SCREEN_W          # ClientAspect = height / width

ZOOM = 1.0


def px_x(units):
    """Layout X units (fraction of the window's width) to pixels."""
    return units * SCREEN_W * ZOOM


def px_y(units):
    """Layout Y units (fraction of the window's height) to pixels."""
    return units * SCREEN_H * ZOOM


def px_len(vertical_units):
    """A *length* given in vertical units, as pixels — the same length on screen either way."""
    return vertical_units * SCREEN_H * ZOOM


# --------------------------------------------------------------------------- geometry
# Every function below mirrors the mod's arithmetic one-for-one. The names are the mod's names.


def card_height(controller):
    return NODE_HEIGHT + (NODE_STATUS_LINE_HEIGHT if controller else 0.0)


def button_row_width(count):
    """PortGeometry.TryCardButtonRow: the row's width in layout X units."""
    return (count * CARD_BUTTON_DIAMETER * ASPECT
            + (count - 1) * (CARD_BUTTON_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT)


def button_row_left(count):
    """Left edge of the row, relative to the card's left edge, in layout X units."""
    right = NODE_WIDTH - (CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * ASPECT
    return right - button_row_width(count)


def switch_rect(controller):
    """PortGeometry.TryPowerSwitchRect, relative to the card's top-left, in layout units.

    The band is a fraction of the column's *inner* height — the card less the padding above and
    below — and the rectangle is inset from the card's top edge by that padding.
    """
    pad = POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX
    inner = card_height(controller) - 2.0 * pad
    ink_h = inner * POWER_SWITCH_BAND_RATIO
    ink_w = ink_h * TOGGLE_INK_ASPECT * ASPECT
    x = (CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * ASPECT
    return x, pad, ink_w, ink_h


def text_span(count, controller, has_switch):
    """PortGeometry.TryCardTextSpan, relative to the card's left edge, in layout X units."""
    right = button_row_left(count) - (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT
    left = NODE_WIDTH * CARD_TEXT_PADDING_RATIO
    if has_switch:
        sx, _, sw, _ = switch_rect(controller)
        left = sx + sw + (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT
    return left, right


def name_fraction(count, controller, has_switch, aspect):
    """Share of the card's width left to the nameplate, at an arbitrary aspect."""
    right = (NODE_WIDTH - (CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * aspect
             - (count * CARD_BUTTON_DIAMETER * aspect
                + (count - 1) * (CARD_BUTTON_GAP_PX / REFERENCE_HEIGHT_PX) * aspect)
             - (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * aspect)
    left = NODE_WIDTH * CARD_TEXT_PADDING_RATIO
    if has_switch:
        pad = POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX
        ink_h = (card_height(controller) - 2.0 * pad) * POWER_SWITCH_BAND_RATIO
        left = ((CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * aspect
                + ink_h * TOGGLE_INK_ASPECT * aspect
                + (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * aspect)
    return (right - left) / NODE_WIDTH


# --------------------------------------------------------------------------- drawing
def load(name):
    image = Image.open(os.path.join(TEXTURES, name + '.png'))
    return image.convert('RGBA') if image.mode != 'RGBA' else image


TEXTURES_BY_NAME = {n: load(n) for n in
                    ('ToggleOn', 'ToggleOff', 'SquarePlus', 'Bell', 'LightbulbSolid',
                     'LightbulbRegular', 'ShareFromSquare', 'CircleDot', 'Xmark')}


def tinted(name, ink_h_px, colour):
    scale = TOGGLE_TEXTURE_SCALE if name in ('ToggleOn', 'ToggleOff') else ICON_TEXTURE_SCALE
    side = max(1, int(round(ink_h_px * scale)))
    alpha = TEXTURES_BY_NAME[name].getchannel('A').resize((side, side), Image.BOX)
    out = Image.new('RGBA', (side, side), colour[:3] + (255,))
    out.putalpha(alpha)
    return out


def draw_card(canvas, left, top, controller, icons, switch_state, integrity, labels):
    """Draws one card exactly as DrawNode does, and returns the part rectangles for labelling."""
    ch = card_height(controller)
    w = px_x(NODE_WIDTH)
    h = px_y(ch)
    draw = ImageDraw.Draw(canvas)

    draw.rectangle([left, top, left + w, top + h], fill=COL_NODE, outline=COL_NODE_BORDER)

    # Damage band: right-anchored, width = 1 - integrity (ADR-0028).
    if integrity < 1.0:
        bw = w * (1.0 - integrity)
        layer = Image.new('RGBA', (max(1, int(bw)), max(1, int(h))), COL_DAMAGE)
        canvas.alpha_composite(layer, (int(left + w - bw), int(top)))

    # State column: the switch in the upper 40%.
    sx, sy, sw, sh = switch_rect(controller)
    if switch_state is not None:
        colour, icon = switch_state
        piece = tinted(icon, px_y(sh), colour)
        cx = left + px_x(sx) + px_x(sw) / 2.0
        cy = top + px_y(sy) + px_y(sh) / 2.0
        canvas.alpha_composite(piece, (int(round(cx - piece.width / 2.0)),
                                       int(round(cy - piece.height / 2.0))))

    # Button row: right-anchored, vertically centred (ADR-0020).
    count = len(icons)
    half_w = px_x(CARD_BUTTON_DIAMETER * 0.5 * ASPECT)
    gap = px_x((CARD_BUTTON_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT)
    stride = half_w * 2 + gap
    right = left + w - px_x((CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * ASPECT)
    diameter = px_len(CARD_BUTTON_DIAMETER)
    for index, name in enumerate(icons):
        from_right = count - 1 - index
        cx = right - half_w - from_right * stride
        cy = top + h * 0.5
        piece = tinted(name, diameter * CARD_BUTTON_ICON_RATIO, COL_PLUS_FILL)
        canvas.alpha_composite(piece, (int(round(cx - piece.width / 2.0)),
                                       int(round(cy - piece.height / 2.0))))

    # Nameplate: the span from TryCardTextSpan, lines placed by equal bands.
    sl, sr = text_span(count, controller, switch_state is not None)
    tl, tr = left + px_x(sl), left + px_x(sr)
    lines = 3 if controller else 2
    band = h / lines
    if tr > tl:
        draw.text((tl + 2, top + band * 0.10 + 2), labels[0], fill=COL_TEXT)
        draw.text((tl + 2, top + band * 1.10 + 2), labels[1], fill=COL_TEXT_DIM)
        if controller:
            draw.text((tl + 2, top + band * 2.10 + 2), labels[2], fill=COL_WATCH)
    return dict(card=(left, top, w, h), switch=(left + px_x(sx), top + px_y(sy),
                                                px_x(sw), px_y(sh)),
                row=(right - count * half_w * 2 - (count - 1) * gap, top + h / 2 - diameter / 2,
                     count * half_w * 2 + (count - 1) * gap, diameter),
                span=(tl, tr), lines=lines)


def arrow_h(draw, x0, x1, y, colour):
    draw.line([x0, y, x1, y], fill=colour, width=1)
    for x, s in ((x0, 1), (x1, -1)):
        draw.line([x, y, x + 4 * s, y - 3], fill=colour, width=1)
        draw.line([x, y, x + 4 * s, y + 3], fill=colour, width=1)


def arrow_v(draw, y0, y1, x, colour):
    draw.line([x, y0, x, y1], fill=colour, width=1)
    for y, s in ((y0, 1), (y1, -1)):
        draw.line([x, y, x - 3, y + 4 * s], fill=colour, width=1)
        draw.line([x, y, x + 3, y + 4 * s], fill=colour, width=1)


def main():
    ink = (150, 170, 195, 255)
    dim = (110, 125, 145, 255)

    panel_w = int(px_x(NODE_WIDTH)) + 470
    panel_h = 250
    rows = [
        ('block · 2 lines · switch + 2 buttons', False, ['SquarePlus'], (COL_POWER_ON, 'ToggleOn'), 1.0,
         ('Timer Block', 'TimerBlockLarge', '')),
        ('block · 1 button (no bind)', False, ['SquarePlus'], (COL_POWER_OFF, 'ToggleOff'), 1.0,
         ('Conveyor Tube', 'LargeBlockConveyor', '')),
        ('block · damaged 35%', False, ['SquarePlus'], (COL_POWER_ON, 'ToggleOn'), 0.35,
         ('Timer Block', 'TimerBlockLarge', '')),
        ('controller · 3 lines · 4 buttons', True,
         ['ShareFromSquare', 'Bell', 'LightbulbSolid', 'LightbulbRegular'],
         (COL_POWER_ON, 'ToggleOn'), 1.0,
         ('Event Controller', 'EventControllerLarge', 'Event: Block Damaged')),
    ]

    sheet = Image.new('RGBA', (panel_w, panel_h * len(rows) + 60), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)

    for index, (label, controller, icons, switch_state, integrity, texts) in enumerate(rows):
        top = 50 + index * panel_h
        left = 40
        draw.text((left, top - 26), label, fill=(150, 165, 185, 255))

        parts = draw_card(sheet, left, top, controller, icons, switch_state, integrity, texts)
        cx, cy, cw, chh = parts['card']
        sx, sy, sw, sh = parts['switch']
        rx, ry, rw, rh = parts['row']
        tl, tr = parts['span']

        # ---- state column callout ----
        draw.rectangle([sx - 1, sy - 1, sx + sw + 1, sy + sh + 1], outline=(110, 215, 130, 255))
        draw.line([sx + sw + 2, sy + sh / 2, cx + cw + 60, sy + sh / 2], fill=dim, width=1)
        draw.text((cx + cw + 66, sy + sh / 2 - 16),
                  'STATE COLUMN', fill=(110, 215, 130, 255))
        draw.text((cx + cw + 66, sy + sh / 2 - 2),
                  'band %.0f%% of inner = %.1f px high' % (POWER_SWITCH_BAND_RATIO * 100, sh), fill=ink)
        draw.text((cx + cw + 66, sy + sh / 2 + 12),
                  'width = 1.5 x band = %.1f px' % sw, fill=ink)
        draw.text((cx + cw + 66, sy + sh / 2 + 26),
                  'pad %.1f px top and bottom' % px_y(POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX), fill=dim)
        draw.text((cx + cw + 66, sy + sh / 2 + 40),
                  'reserved below: %.1f px' % (chh - sh - 2 * px_y(POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX)), fill=dim)

        # ---- nameplate callout ----
        my = cy + chh + 34
        arrow_h(draw, tl, tr, my, (226, 232, 240, 255))
        draw.text((tl, my + 6), 'NAMEPLATE %.1f px (%.0f%% of the card), %d equal bands'
                  % (tr - tl, 100.0 * (tr - tl) / cw, parts['lines']), fill=(226, 232, 240, 255))

        # ---- button row callout ----
        by = cy - 22
        arrow_h(draw, rx, rx + rw, by, (255, 214, 140, 255))
        draw.text((rx - 4, by - 18), 'BUTTON ROW  d=%.1f px  ink=%.1f px'
                  % (rh, rh * CARD_BUTTON_ICON_RATIO), fill=(255, 214, 140, 255))

        # ---- card height callout ----
        arrow_v(draw, cy, cy + chh, cx - 18, dim)
        draw.text((cx - 16, cy + chh / 2), '  %.1f px' % chh, fill=dim)
        draw.text((cx - 16, cy + chh / 2 + 14), '  (%.3f)' % card_height(controller), fill=dim)

        # ---- damage band note ----
        if integrity < 1.0:
            draw.text((cx + 4, cy + chh + 6), 'damage band: right %.0f%% (integrity %.0f%%)'
                      % ((1.0 - integrity) * 100, integrity * 100), fill=(200, 120, 110, 255))

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert('RGB').save(OUT)
    print('wrote', OUT, sheet.size)

    # ---- the printed table: the numbers the picture was drawn from ----
    print()
    print('window %dx%d, aspect %.4f, zoom %.0f%%' % (SCREEN_W, SCREEN_H, ASPECT, ZOOM * 100))
    print('card: %.1f x %.1f px (block) / %.1f x %.1f px (controller)'
          % (px_x(NODE_WIDTH), px_y(NODE_HEIGHT), px_x(NODE_WIDTH),
             px_y(card_height(True))))
    print('button: d %.1f px, ink %.1f px, gap %.1f px, margin %.1f px'
          % (px_len(CARD_BUTTON_DIAMETER), px_len(CARD_BUTTON_DIAMETER) * CARD_BUTTON_ICON_RATIO,
             px_x((CARD_BUTTON_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT),
             px_x((CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * ASPECT)))
    print('switch: %.1f x %.1f px (band %.0f%% of inner, pad %.1f px, ink aspect %.1f)'
          % (px_x(switch_rect(False)[2]), px_y(switch_rect(False)[3]),
             POWER_SWITCH_BAND_RATIO * 100, px_y(POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX),
             TOGGLE_INK_ASPECT))
    print()
    print('nameplate share of the card width, 4-button controller card:')
    print('  %-8s %-12s %-12s' % ('aspect', 'no switch', 'with switch'))
    for aspect, name in ((0.5625, '16:9'), (0.625, '16:10'), (0.75, '4:3'), (1.0, 'square')):
        a = name_fraction(4, True, False, aspect)
        b = name_fraction(4, True, True, aspect)
        print('  %-8s %-12.1f %-12.1f' % (name, a * 100, b * 100))
    print()
    print('aspect at which the name keeps the 30%% floor (4 buttons):')
    lo, hi = 0.2, 3.0
    for _ in range(60):
        mid = (lo + hi) / 2.0
        if name_fraction(4, True, True, mid) > 0.30:
            lo = mid
        else:
            hi = mid
    print('  with switch: %.3f' % lo)
    lo2, hi2 = 0.2, 3.0
    for _ in range(60):
        mid = (lo2 + hi2) / 2.0
        if name_fraction(4, True, False, mid) > 0.30:
            lo2 = mid
        else:
            hi2 = mid
    print('  without switch: %.3f' % lo2)


if __name__ == '__main__':
    main()
