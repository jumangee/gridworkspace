"""Draws the four state widgets at a legible size, from the mod's own constants (ADR-0042).

Why a second figure: on the card the widget is a 21 px square inside a 307 px card, and neither its
2 px inset nor the angle of a clock hand can be judged at that size. The card sheet answers "where
is the widget"; this one answers "what does it look like", at 12x.

**The numbers come from `VgsConstants.cs` through the same parser the card sheet uses** — this file
imports `card_layout_preview` rather than copying anything. What is drawn here is therefore the
geometry the mod computes, not a drawing of it; the printed table is the same numbers in pixels.

Run: python tools/icon-atlas/widget_zoom.py
"""

import os
import math

from PIL import Image, ImageDraw

import card_layout_preview as layout

ROOT = layout.ROOT
OUT = os.path.join(ROOT, r'docs\01-research\images\card-widget-zoom.png')

# The sheet is drawn at a large zoom so the shapes are legible; every number printed below is at
# 100% and 1920x1080, which is what the ADR quotes.
ZOOM = 12.0

COL_WIDGET = layout.COL_WIDGET
COL_RECHARGE = layout.COL_BATTERY_RECHARGE
COL_DISCHARGE = layout.COL_BATTERY_DISCHARGE
COL_BACKDROP = (12, 15, 20, 255)
COL_CARD = (38, 45, 58, 255)
COL_LABEL = (226, 232, 240, 255)
COL_NOTE = (140, 152, 170, 255)


def px_x(units):
    return units * layout.SCREEN_W * ZOOM


def px_y(units):
    return units * layout.SCREEN_H * ZOOM


def draw_widget(draw, canvas, left, top, kind, values, accent):
    """One widget whose *area* starts at (left, top), in the same arithmetic the mod uses.

    `widget_bars`/`widget_clock`/`widget_bolt` return card-relative coordinates, so the origin is
    shifted by the area's own position: a caller that pasted the raw numbers would draw the widget
    one band too low, which is exactly the defect this sheet exists to catch.
    """
    controller = False
    ax, ay, _, _ = layout.widget_area(controller)
    left -= px_x(ax)
    top -= px_y(ay)

    if kind == 'clock':
        cx, cy, radius = layout.widget_clock(controller)
        centre = (left + px_x(cx), top + px_y(cy))
        r = px_y(radius)

        for i in range(24):
            a0 = -math.pi * 0.5 + i / 24.0 * math.pi * 2.0
            a1 = -math.pi * 0.5 + (i + 1) / 24.0 * math.pi * 2.0
            draw.line([centre[0] + math.cos(a0) * r, centre[1] + math.sin(a0) * r,
                       centre[0] + math.cos(a1) * r, centre[1] + math.sin(a1) * r],
                      fill=COL_WIDGET, width=max(1, int(round(px_y(
                          layout.WIDGET_RING_PX / layout.REFERENCE_HEIGHT_PX)))))

        for ratio, turns in ((layout.WIDGET_HOUR_HAND_RATIO, 0.0),
                             (layout.WIDGET_MINUTE_HAND_RATIO, values[0])):
            tip = layout.hand_tip((cx, cy), radius, ratio, turns)
            draw.line([centre[0], centre[1], left + px_x(tip[0]), top + px_y(tip[1])],
                      fill=COL_WIDGET,
                      width=max(1, int(round(px_y(layout.WIDGET_HAND_PX
                                                  / layout.REFERENCE_HEIGHT_PX)))))
        return

    count = 2 if kind == 'production' else 1
    for frame, fill in layout.widget_bars(controller, kind, count, values):
        fx, fy, fw, fh = frame
        draw.rectangle([left + px_x(fx), top + px_y(fy),
                        left + px_x(fx + fw), top + px_y(fy + fh)], outline=COL_WIDGET)
        if fill[3] > 0.0 and fill[2] > 0.0:
            gx, gy, gw, gh = fill
            draw.rectangle([left + px_x(gx), top + px_y(gy),
                            left + px_x(gx + gw), top + px_y(gy + gh)], fill=COL_WIDGET)

    if kind == 'battery' and accent is not None:
        cx, cy, ink_h = layout.widget_bolt(controller)
        colour = COL_RECHARGE if accent == 'recharge' else COL_DISCHARGE
        piece = layout.tinted('Bolt', px_y(ink_h), colour)
        canvas.alpha_composite(piece, (int(round(left + px_x(cx) - piece.width / 2.0)),
                                       int(round(top + px_y(cy) - piece.height / 2.0))))


def main():
    _, _, area_w, area_h = layout.widget_area(False)
    cell_w = int(px_x(area_w)) + 40
    cell_h = int(px_y(area_h)) + 66

    rows = [
        ('cargo container · 45% full', 'fill', [0.45], None),
        ('cargo container · empty', 'fill', [0.0], None),
        ('battery · 70%, recharging', 'battery', [0.70], 'recharge'),
        ('battery · 35%, discharging', 'battery', [0.35], 'discharge'),
        ('timer · 55% elapsed', 'clock', [0.55], None),
        ('timer · 10% elapsed', 'clock', [0.10], None),
        ('refinery · in 30%, out 80%', 'production', [0.30, 0.80], None),
        ('refinery · both empty', 'production', [0.0, 0.0], None),
    ]

    sheet = Image.new('RGBA', (cell_w * 4, cell_h * 2 + 30), COL_BACKDROP)
    draw = ImageDraw.Draw(sheet)

    for index, (label, kind, values, accent) in enumerate(rows):
        column = index % 4
        row = index // 4
        left = column * cell_w + 20
        top = row * cell_h + 40

        # The widget's own area, so the padding is visible as a frame around the frame.
        ax, ay, aw, ah = layout.widget_area(False)
        draw.rectangle([left, top, left + px_x(aw), top + px_y(ah)],
                       fill=COL_CARD, outline=(60, 70, 86, 255))

        draw_widget(draw, sheet, left, top, kind, values, accent)
        draw.text((left, top - 18), label, fill=COL_LABEL)

    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    sheet.convert('RGB').save(OUT)
    print('wrote', OUT, sheet.size)

    # ---- the numbers, at 100% ----
    layout.ZOOM = 1.0
    ax, ay, aw, ah = layout.widget_area(False)
    print()
    print('widget area at 100%%: %.1f x %.1f px' % (layout.px_x(aw), layout.px_y(ah)))

    for label, kind, values, accent in rows:
        count = 2 if kind == 'production' else 1
        if kind == 'clock':
            cx, cy, radius = layout.widget_clock(False)
            print('  %-32s face %.1f px across, hands %.0f%% / %.0f%% of the radius'
                  % (label, layout.px_y(radius) * 2.0,
                     layout.WIDGET_HOUR_HAND_RATIO * 100, layout.WIDGET_MINUTE_HAND_RATIO * 100))
            continue

        parts = layout.widget_bars(False, kind, count, values)
        frame, fill = parts[0]
        text = ('  %-32s frame %.1f x %.1f px, fill %.1f x %.1f px'
                % (label, layout.px_x(frame[2]), layout.px_y(frame[3]),
                   layout.px_x(fill[2]), layout.px_y(fill[3])))
        if count == 2:
            second = parts[1]
            text += ', second bar at %.1f px' % layout.px_x(second[0][0] - frame[0] - frame[2])
        print(text)


if __name__ == '__main__':
    main()
