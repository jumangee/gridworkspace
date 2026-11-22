"""Compares the prototype rasteriser with FreeType's rendering of the same OTF glyph.

FreeType is not the target renderer, but it is an independent implementation of the same
geometry: if the arcs sweep the right way and the fill rule is right, the two masks must agree
in area and in shape. A wrong sweep would show up as a mirrored or missing arc, and a wrong
fill rule would fill the hole in `circle-dot`.
"""

import os
import numpy as np
from PIL import Image, ImageFont, ImageDraw

ROOT = r'e:\Projects\!VGS'
FA = os.path.join(ROOT, r'data\fontawesome-free-7.3.1-desktop')
OUT = os.path.join(ROOT, r'tmp\icon-proto\out')

SPECS = [
    ('circle-dot-solid', r'otfs\Font Awesome 7 Free-Solid-900.otf', 0xF192),
    ('circle-dot-regular', r'otfs\Font Awesome 7 Free-Regular-400.otf', 0xF192),
    ('square-plus-regular', r'otfs\Font Awesome 7 Free-Regular-400.otf', 0xF0FE),
    ('xmark-solid', r'otfs\Font Awesome 7 Free-Solid-900.otf', 0xF00D),
]


def ft_alpha(otf, codepoint, px):
    font = ImageFont.truetype(otf, px)
    image = Image.new('L', (px * 2, px * 2), 0)
    ImageDraw.Draw(image).text((px // 2, px // 2), chr(codepoint), font=font, fill=255)
    array = np.array(image, dtype=np.float32)
    ys, xs = np.nonzero(array > 8)
    return array[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def our_alpha(name, px):
    array = np.array(Image.open(os.path.join(OUT, '%s-%d.pgm' % (name, px))), dtype=np.float32)
    ys, xs = np.nonzero(array > 8)
    return array[ys.min():ys.max() + 1, xs.min():xs.max() + 1]


def main():
    for name, otf, codepoint in SPECS:
        for px in (29, 44):
            ref = ft_alpha(os.path.join(FA, otf), codepoint, px)
            ours = our_alpha(name, px)

            ref_image = Image.fromarray(ref.astype(np.uint8)).resize(
                (ours.shape[1], ours.shape[0]), Image.LANCZOS)
            ref_scaled = np.array(ref_image, dtype=np.float32)

            best = None
            for dy in (-1, 0, 1):
                for dx in (-1, 0, 1):
                    shifted = np.roll(np.roll(ref_scaled, dy, axis=0), dx, axis=1)
                    diff = np.abs(shifted - ours).mean()
                    if best is None or diff < best[0]:
                        best = (diff, dy, dx)

            ref_area = ref_scaled.sum() / 255.0
            our_area = ours.sum() / 255.0
            delta = 100.0 * (our_area - ref_area) / max(ref_area, 1.0)
            print('%-22s %3dpx  bbox %dx%d  area ours %7.1f  freetype %7.1f (%+5.1f%%)  '
                  'mean|diff| %5.1f/255' % (
                      name, px, ours.shape[1], ours.shape[0], our_area, ref_area, delta, best[0]))


if __name__ == '__main__':
    main()
