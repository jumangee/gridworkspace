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
# ---- the state widget (ADR-0042) ----
WIDGET_PAD_FILL_W = C['WidgetPaddingFillWidth']
WIDGET_PAD_FILL_H = C['WidgetPaddingFillHeight']
WIDGET_PAD_W = C['WidgetPaddingWidth']
WIDGET_PAD_H = C['WidgetPaddingHeight']
WIDGET_PAD_CLOCK = C['WidgetPaddingClock']
WIDGET_INSET_PX = C['WidgetInsetPixels']
WIDGET_BAR_GAP_PX = C['WidgetBarGapPixels']
WIDGET_BOLT_SHARE = C['WidgetBoltShare']
WIDGET_BOLT_GAP_PX = C['WidgetBoltGapPixels']
WIDGET_RING_PX = C['WidgetRingPixels']
WIDGET_HAND_PX = C['WidgetHandPixels']
WIDGET_HOUR_HAND_RATIO = C['WidgetHourHandRatio']
WIDGET_MINUTE_HAND_RATIO = C['WidgetMinuteHandRatio']
WIDGET_TOP_GAP_PX = C['WidgetTopGapPixels']
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
BOLT_INK_ASPECT = 0.823          # Bolt ink 448 x 544
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
COL_WIDGET = (119, 119, 119, 255)
COL_BATTERY_RECHARGE = (240, 200, 90, 255)
COL_BATTERY_DISCHARGE = (255, 90, 90, 255)

# The window the numbers are quoted for. 16:9 is what the editor is normally used in, and the
# aspect is what turns the layout's vertical units into horizontal ones.
SCREEN_W = 1920.0
SCREEN_H = 1080.0
ASPECT = SCREEN_H / SCREEN_W          # ClientAspect = height / width

# The *picture* is drawn at 2x. At 100% the state widget is a 21 px square inside a 307 px card,
# and neither its 2 px inset nor its hands can be judged at that size — a preview that cannot show
# the thing it was drawn for is not a preview. The printed table is quoted at 100%, so `main()`
# resets this before printing (see the comment there).
ZOOM = 2.0


def px_x(units):
    """Layout X units (fraction of the window's width) to pixels."""
    return units * SCREEN_W * ZOOM


def px_y(units):
    """Layout Y units (fraction of the window's height) to pixels."""
    return units * SCREEN_H * ZOOM


def px_len(vertical_units):
    """A *length* given in vertical units, as pixels — the same length on screen either way."""
    return vertical_units * SCREEN_H * ZOOM


def px_ref_x(units):
    """The same as `px_x`, but at the reference zoom — for the labels.

    The sheet is drawn at 2x so the widget is legible, while every number the ADR quotes is at
    100%. A label printed from `px_x` would therefore claim the column is 41.9 px when it is 20.9,
    and a reader checking the ADR against the picture would find a factor of two that is not there.
    """
    return units * SCREEN_W


def px_ref_y(units):
    """The same as `px_y`, but at the reference zoom. See `px_ref_x`."""
    return units * SCREEN_H


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


def column_width(controller):
    """PortGeometry.StateColumnWidth, in vertical units: the wider of the icon and the band."""
    pad = POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX
    inner = card_height(controller) - 2.0 * pad
    band = inner * (1.0 - POWER_SWITCH_BAND_RATIO)
    ink = inner * POWER_SWITCH_BAND_RATIO * TOGGLE_INK_ASPECT
    return max(band, ink)


def widget_area(controller):
    """PortGeometry.TryStateWidgetArea, relative to the card's top-left, in layout units.

    The lower band of the column, less the gap under the switch: as wide as the column and one gap
    shorter than that, so the two do not touch. The area is therefore slightly wider than it is
    tall, and the shapes are inscribed in it rather than stretched (ADR-0042 §3a).
    """
    pad = POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX
    inner = card_height(controller) - 2.0 * pad
    band = inner * (1.0 - POWER_SWITCH_BAND_RATIO) - WIDGET_TOP_GAP_PX / REFERENCE_HEIGHT_PX
    x = (CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * ASPECT
    y = pad + inner - band
    return x, y, column_width(controller) * ASPECT, band


def widget_padding(kind):
    """PortGeometry.StateWidgetPadding: shares of the area, both sides together."""
    if kind == 'fill':
        return WIDGET_PAD_FILL_W, WIDGET_PAD_FILL_H
    if kind == 'clock':
        return WIDGET_PAD_CLOCK, WIDGET_PAD_CLOCK
    return WIDGET_PAD_W, WIDGET_PAD_H


def widget_bars(controller, kind, count, values):
    """PortGeometry.TryStateWidgetBar for each bar: (frame, fill) in layout units, card-relative."""
    ax, ay, aw, ah = widget_area(controller)
    pad_x, pad_y = widget_padding(kind)

    left = ax + aw * pad_x * 0.5
    top = ay + ah * pad_y * 0.5
    width = aw * (1.0 - pad_x)
    height = ah * (1.0 - pad_y)

    if kind == 'battery':
        width = width - width * WIDGET_BOLT_SHARE - (WIDGET_BOLT_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT

    gap = (WIDGET_BAR_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT if count > 1 else 0.0
    bar_w = (width - gap * (count - 1)) / count

    inset_x = (WIDGET_INSET_PX / REFERENCE_HEIGHT_PX) * ASPECT
    inset_y = WIDGET_INSET_PX / REFERENCE_HEIGHT_PX

    out = []
    for index in range(count):
        fx = left + index * (bar_w + gap)
        inner_w = bar_w - 2.0 * inset_x
        inner_h = height - 2.0 * inset_y
        value = max(0.0, min(1.0, values[index] if index < len(values) else 0.0))
        filled = inner_h * value
        out.append(((fx, top, bar_w, height),
                    (fx + inset_x, top + height - inset_y - filled, inner_w, filled)))
    return out


def widget_bolt(controller):
    """PortGeometry.TryStateWidgetBolt: centre and ink height, card-relative, layout units.

    The column is a *width* in horizontal units; the ink's height is a vertical length, so the
    conversion goes through the aspect before the icon's own aspect divides it.
    """
    ax, ay, aw, ah = widget_area(controller)
    pad_x, pad_y = widget_padding('battery')
    width = aw * (1.0 - pad_x)
    height = ah * (1.0 - pad_y)
    column = width * WIDGET_BOLT_SHARE
    ink_h = column / (ASPECT * BOLT_INK_ASPECT)
    cx = ax + aw * (1.0 - pad_x * 0.5) - column * 0.5
    cy = ay + ah * pad_y * 0.5 + height * 0.5
    return cx, cy, ink_h


def widget_clock(controller):
    """PortGeometry.TryStateWidgetClock: centre and radius in vertical units, card-relative."""
    ax, ay, aw, ah = widget_area(controller)
    pad = WIDGET_PAD_CLOCK
    diameter = min(aw * (1.0 - pad) / ASPECT, ah * (1.0 - pad))
    return ax + aw * 0.5, ay + ah * 0.5, diameter * 0.5


def hand_tip(center, radius, ratio, turns):
    """PortGeometry.StateWidgetHandTip: twelve o'clock at zero, clockwise, aspect-corrected."""
    import math
    angle = -math.pi * 0.5 + turns * math.pi * 2.0
    reach = radius * ratio
    return (center[0] + math.cos(angle) * reach * ASPECT,
            center[1] + math.sin(angle) * reach)


def text_span(count, controller, has_switch):
    """PortGeometry.TryCardTextSpan, relative to the card's left edge, in layout X units."""
    right = button_row_left(count) - (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT
    left = NODE_WIDTH * CARD_TEXT_PADDING_RATIO
    if has_switch:
        sx, _, sw, _ = switch_rect(controller)
        left = sx + sw + (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT
    return left, right


def name_fraction(count, controller, has_column, aspect):
    """Share of the card's width left to the nameplate, at an arbitrary aspect.

    `has_column` is "the card has a state column" (a switch, a widget, or both), which is what
    indents the nameplate (ADR-0042 §1). The column's width is the same either way: the band and
    the switch icon give one number today (1.5 x 0.4 = 0.6).
    """
    right = (NODE_WIDTH - (CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * aspect
             - (count * CARD_BUTTON_DIAMETER * aspect
                + (count - 1) * (CARD_BUTTON_GAP_PX / REFERENCE_HEIGHT_PX) * aspect)
             - (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * aspect)
    left = NODE_WIDTH * CARD_TEXT_PADDING_RATIO
    if has_column:
        left = ((CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * aspect
                + column_width(controller) * aspect
                + (CARD_BUTTON_TEXT_GAP_PX / REFERENCE_HEIGHT_PX) * aspect)
    return (right - left) / NODE_WIDTH


# --------------------------------------------------------------------------- drawing
def load(name):
    image = Image.open(os.path.join(TEXTURES, name + '.png'))
    return image.convert('RGBA') if image.mode != 'RGBA' else image


TEXTURES_BY_NAME = {n: load(n) for n in
                    ('ToggleOn', 'ToggleOff', 'SquarePlus', 'Bell', 'LightbulbSolid',
                     'LightbulbRegular', 'ShareFromSquare', 'CircleDot', 'Xmark', 'Bolt')}


def draw_widget(canvas, draw, left, top, controller, widget):
    """Draws the state widget exactly as VgsEditorScreen.DrawStateWidget does (ADR-0042).

    A frame is a one-pixel outline and a fill is a solid rectangle inside it; the clock is a ring
    of 24 chords plus two hands. Everything is in the base grey except the battery's bolt, which
    carries the accent.
    """
    kind, values, accent = widget

    def rect(box):
        x, y, bw, bh = box
        return [left + px_x(x), top + px_y(y), left + px_x(x + bw), top + px_y(y + bh)]

    if kind == 'clock':
        cx, cy, radius = widget_clock(controller)
        centre = (left + px_x(cx), top + px_y(cy))
        r_px = px_y(radius)

        import math
        for i in range(24):
            a0 = -math.pi * 0.5 + i / 24.0 * math.pi * 2.0
            a1 = -math.pi * 0.5 + (i + 1) / 24.0 * math.pi * 2.0
            # r_px is a vertical length turned into pixels, and a pixel is square on screen, so
            # the horizontal reach is the same number: the face is round, not an ellipse.
            draw.line([centre[0] + math.cos(a0) * r_px, centre[1] + math.sin(a0) * r_px,
                       centre[0] + math.cos(a1) * r_px, centre[1] + math.sin(a1) * r_px],
                      fill=COL_WIDGET, width=1)

        for ratio, turns in ((WIDGET_HOUR_HAND_RATIO, 0.0),
                             (WIDGET_MINUTE_HAND_RATIO, values[0])):
            tip = hand_tip((cx, cy), radius, ratio, turns)
            draw.line([centre[0], centre[1], left + px_x(tip[0]), top + px_y(tip[1])],
                      fill=COL_WIDGET, width=max(1, int(round(WIDGET_HAND_PX))))
        return

    count = 2 if kind == 'production' else 1
    for frame, fill in widget_bars(controller, kind, count, values):
        draw.rectangle(rect(frame), outline=COL_WIDGET)
        if fill[3] > 0.0 and fill[2] > 0.0:
            draw.rectangle(rect(fill), fill=COL_WIDGET)

    if kind == 'battery' and accent is not None:
        cx, cy, ink_h = widget_bolt(controller)
        colour = COL_BATTERY_RECHARGE if accent == 'recharge' else COL_BATTERY_DISCHARGE
        piece = tinted('Bolt', px_y(ink_h), colour)
        canvas.alpha_composite(piece, (int(round(left + px_x(cx) - piece.width / 2.0)),
                                       int(round(top + px_y(cy) - piece.height / 2.0))))


def tinted(name, ink_h_px, colour):
    scale = TOGGLE_TEXTURE_SCALE if name in ('ToggleOn', 'ToggleOff') else ICON_TEXTURE_SCALE
    side = max(1, int(round(ink_h_px * scale)))
    alpha = TEXTURES_BY_NAME[name].getchannel('A').resize((side, side), Image.BOX)
    out = Image.new('RGBA', (side, side), colour[:3] + (255,))
    out.putalpha(alpha)
    return out


def draw_card(canvas, left, top, controller, icons, switch_state, integrity, labels, widget=None):
    """Draws one card exactly as DrawNode does, and returns the part rectangles for labelling.

    `widget` is (kind, [values], accent) or None; kinds are 'fill', 'battery', 'clock' and
    'production', and `accent` is 'recharge', 'discharge' or None (ADR-0042).
    """
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

    # State column: the switch in the upper 40%, the widget in the lower 60% (ADR-0042).
    sx, sy, sw, sh = switch_rect(controller)
    if switch_state is not None:
        colour, icon = switch_state
        piece = tinted(icon, px_y(sh), colour)
        cx = left + px_x(sx) + px_x(sw) / 2.0
        cy = top + px_y(sy) + px_y(sh) / 2.0
        canvas.alpha_composite(piece, (int(round(cx - piece.width / 2.0)),
                                       int(round(cy - piece.height / 2.0))))

    if widget is not None:
        draw_widget(canvas, draw, left, top, controller, widget)

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

    # Nameplate: the span from TryCardTextSpan, lines placed by equal bands. The column exists
    # when there is a switch OR a widget — a cargo container has the widget and no switch
    # (ADR-0042 §1), which is the case that separated the two questions.
    has_column = switch_state is not None or widget is not None
    sl, sr = text_span(count, controller, has_column)
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

    panel_w = int(px_x(NODE_WIDTH)) + 560
    panel_h = 400
    rows = [
        ('block · 2 lines · switch + clock', False, ['SquarePlus'], (COL_POWER_ON, 'ToggleOn'), 1.0,
         ('Timer Block', 'TimerBlockLarge', ''), ('clock', [0.55], None)),
        ('cargo container · widget, no switch', False, ['ShareFromSquare'], None, 1.0,
         ('Cargo Container', 'LargeBlockCargoContainer', ''), ('fill', [0.45], None)),
        ('battery · recharging', False, ['ShareFromSquare'], (COL_POWER_ON, 'ToggleOn'), 1.0,
         ('Battery', 'LargeBlockBatteryBlock', ''), ('battery', [0.70], 'recharge')),
        ('refinery · input / output', False, ['ShareFromSquare'], (COL_POWER_ON, 'ToggleOn'), 1.0,
         ('Refinery', 'LargeBlockRefinery', ''), ('production', [0.30, 0.80], None)),
        ('block · 1 button (no bind)', False, ['SquarePlus'], (COL_POWER_OFF, 'ToggleOff'), 1.0,
         ('Conveyor Tube', 'LargeBlockConveyor', ''), None),
        ('block · damaged 35%', False, ['SquarePlus'], (COL_POWER_ON, 'ToggleOn'), 0.35,
         ('Timer Block', 'TimerBlockLarge', ''), None),
        ('controller · 3 lines · 4 buttons', True,
         ['ShareFromSquare', 'Bell', 'LightbulbSolid', 'LightbulbRegular'],
         (COL_POWER_ON, 'ToggleOn'), 1.0,
         ('Event Controller', 'EventControllerLarge', 'Event: Block Damaged'), None),
    ]

    sheet = Image.new('RGBA', (panel_w, panel_h * len(rows) + 60), COL_CANVAS)
    draw = ImageDraw.Draw(sheet)
    draw.text((40, 12),
              'Grid Workspace · card layout · drawn at %dx, every number in px at 100%% (1920x1080)'
              % int(ZOOM), fill=(150, 165, 185, 255))

    for index, (label, controller, icons, switch_state, integrity, texts, widget) in enumerate(rows):
        top = 80 + index * panel_h
        left = 40
        draw.text((left, top - 26), label, fill=(150, 165, 185, 255))

        parts = draw_card(sheet, left, top, controller, icons, switch_state, integrity, texts, widget)
        cx, cy, cw, chh = parts['card']
        sx, sy, sw, sh = parts['switch']
        rx, ry, rw, rh = parts['row']
        tl, tr = parts['span']

        # ---- state column callout ----
        #
        # Every number in these labels is at the *reference* zoom (100%), while the picture is at
        # 2x — otherwise a reader comparing the ADR with the sheet would find a factor of two that
        # is not in the layout. `px_ref_*` exists for exactly this.
        draw.rectangle([sx - 1, sy - 1, sx + sw + 1, sy + sh + 1], outline=(110, 215, 130, 255))
        draw.line([sx + sw + 2, sy + sh / 2, cx + cw + 60, sy + sh / 2], fill=dim, width=1)
        draw.text((cx + cw + 66, sy + sh / 2 - 16),
                  'STATE COLUMN', fill=(110, 215, 130, 255))
        draw.text((cx + cw + 66, sy + sh / 2 - 2),
                  'switch band %.0f%% of inner = %.1f px'
                  % (POWER_SWITCH_BAND_RATIO * 100,
                     px_ref_y(switch_rect(controller)[3])), fill=ink)
        draw.text((cx + cw + 66, sy + sh / 2 + 12),
                  'column width = %.1f px' % px_ref_x(column_width(controller) * ASPECT), fill=ink)
        draw.text((cx + cw + 66, sy + sh / 2 + 26),
                  'pad %.1f px top and bottom'
                  % px_ref_y(POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX), fill=dim)

        # ---- widget callout (ADR-0042): its own line, below the column's ----
        if widget is not None:
            wx, wy, ww, wh = widget_area(controller)
            box = [left + px_x(wx), top + px_y(wy), left + px_x(wx + ww), top + px_y(wy + wh)]
            draw.rectangle(box, outline=(200, 160, 255, 255))
            callout_y = sy + sh / 2 + 52
            draw.line([box[2] + 2, box[3] - 2, cx + cw + 60, callout_y + 6], fill=dim, width=1)
            draw.text((cx + cw + 66, callout_y),
                      'STATE WIDGET: %s' % widget[0], fill=(200, 160, 255, 255))
            draw.text((cx + cw + 66, callout_y + 14),
                      '%.1f x %.1f px, %.0f%% band less the %.0f px gap'
                      % (px_ref_x(ww), px_ref_y(wh), (1.0 - POWER_SWITCH_BAND_RATIO) * 100,
                         WIDGET_TOP_GAP_PX), fill=ink)

        # ---- nameplate callout ----
        my = cy + chh + 34
        arrow_h(draw, tl, tr, my, (226, 232, 240, 255))
        draw.text((tl, my + 6), 'NAMEPLATE %.1f px (%.0f%% of the card), %d equal bands'
                  % (tr - tl, 100.0 * (tr - tl) / cw, parts['lines']), fill=(226, 232, 240, 255))

        # ---- button row callout ----
        by = cy - 22
        arrow_h(draw, rx, rx + rw, by, (255, 214, 140, 255))
        draw.text((rx - 4, by - 18), 'BUTTON ROW  d=%.1f px  ink=%.1f px'
                  % (px_ref_y(CARD_BUTTON_DIAMETER), px_ref_y(CARD_BUTTON_DIAMETER)
                     * CARD_BUTTON_ICON_RATIO), fill=(255, 214, 140, 255))

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

    # ---- the printed table: the numbers the picture was drawn from, at 100% ----
    #
    # The picture is at 2x so the widget is legible; every number here is at the reference zoom,
    # which is what the ADR quotes. `px_ref_*` is that conversion, so the two cannot drift.
    print()
    print('window %dx%d, aspect %.4f; numbers below are at 100%% (the sheet is drawn at %dx)'
          % (SCREEN_W, SCREEN_H, ASPECT, int(ZOOM)))
    print('card: %.1f x %.1f px (block) / %.1f x %.1f px (controller)'
          % (px_ref_x(NODE_WIDTH), px_ref_y(NODE_HEIGHT), px_ref_x(NODE_WIDTH),
             px_ref_y(card_height(True))))
    print('button: d %.1f px, ink %.1f px, gap %.1f px, margin %.1f px'
          % (px_ref_y(CARD_BUTTON_DIAMETER), px_ref_y(CARD_BUTTON_DIAMETER) * CARD_BUTTON_ICON_RATIO,
             px_ref_x((CARD_BUTTON_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT),
             px_ref_x((CARD_BUTTON_MARGIN_PX / REFERENCE_HEIGHT_PX) * ASPECT)))
    print('switch: %.1f x %.1f px (band %.0f%% of inner, pad %.1f px, ink aspect %.1f)'
          % (px_ref_x(switch_rect(False)[2]), px_ref_y(switch_rect(False)[3]),
             POWER_SWITCH_BAND_RATIO * 100, px_ref_y(POWER_SWITCH_PADDING_PX / REFERENCE_HEIGHT_PX),
             TOGGLE_INK_ASPECT))

    # ---- the state widget (ADR-0042) ----
    ax, ay, aw, ah = widget_area(False)
    print('widget area: %.1f x %.1f px (lower %.0f%% of the column inner height, less the %.0f px gap '
          'under the switch)'
          % (px_ref_x(aw), px_ref_y(ah), (1.0 - POWER_SWITCH_BAND_RATIO) * 100, WIDGET_TOP_GAP_PX))

    pad_x, pad_y = widget_padding('fill')
    frame_w = aw * (1.0 - pad_x)
    frame_h = ah * (1.0 - pad_y)
    inset_x = (WIDGET_INSET_PX / REFERENCE_HEIGHT_PX) * ASPECT
    inset_y = WIDGET_INSET_PX / REFERENCE_HEIGHT_PX
    print('cargo bar: pad %.0f%% x %.0f%% -> frame %.1f x %.1f px, fill %.1f x %.1f px, inset %.1f px'
          % (pad_x * 100, pad_y * 100, px_ref_x(frame_w), px_ref_y(frame_h),
             px_ref_x(frame_w - 2.0 * inset_x), px_ref_y(frame_h - 2.0 * inset_y),
             px_ref_y(inset_y)))

    pad_x, pad_y = widget_padding('production')
    pair_w = aw * (1.0 - pad_x)
    gap_x = (WIDGET_BAR_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT
    print('production pair: pad %.0f%% x %.0f%% -> each bar %.1f px wide, gap %.1f px'
          % (pad_x * 100, pad_y * 100, px_ref_x((pair_w - gap_x) / 2.0), px_ref_x(gap_x)))

    pad_x, _ = widget_padding('battery')
    bolt_cx, bolt_cy, bolt_h = widget_bolt(False)
    bolt_column = aw * (1.0 - pad_x) * WIDGET_BOLT_SHARE
    print('battery: bar %.1f px wide, bolt %.1f x %.1f px (share %.0f%% of the padded width)'
          % (px_ref_x(aw * (1.0 - pad_x) * (1.0 - WIDGET_BOLT_SHARE)
                      - (WIDGET_BOLT_GAP_PX / REFERENCE_HEIGHT_PX) * ASPECT),
             px_ref_x(bolt_column), px_ref_y(bolt_h), WIDGET_BOLT_SHARE * 100))

    clock_cx, clock_cy, clock_r = widget_clock(False)
    print('clock: face %.1f px across (pad %.0f%%), fixed hand %.0f%% of the radius, moving %.0f%%'
          % (px_ref_y(clock_r) * 2.0, WIDGET_PAD_CLOCK * 100,
             WIDGET_HOUR_HAND_RATIO * 100, WIDGET_MINUTE_HAND_RATIO * 100))
    print()
    print('nameplate share of the card width, 4-button controller card:')
    print('  %-8s %-14s %-14s' % ('aspect', 'no column', 'with column'))
    for aspect, name in ((0.5625, '16:9'), (0.625, '16:10'), (0.75, '4:3'), (1.0, 'square')):
        a = name_fraction(4, True, False, aspect)
        b = name_fraction(4, True, True, aspect)
        print('  %-8s %-14.1f %-14.1f' % (name, a * 100, b * 100))
    print()
    print('aspect at which the name keeps the 30%% floor (4 buttons):')
    lo, hi = 0.2, 3.0
    for _ in range(60):
        mid = (lo + hi) / 2.0
        if name_fraction(4, True, True, mid) > 0.30:
            lo = mid
        else:
            hi = mid
    print('  with column: %.3f' % lo)
    lo2, hi2 = 0.2, 3.0
    for _ in range(60):
        mid = (lo2 + hi2) / 2.0
        if name_fraction(4, True, False, mid) > 0.30:
            lo2 = mid
        else:
            hi2 = mid
    print('  without column: %.3f' % lo2)


if __name__ == '__main__':
    main()
