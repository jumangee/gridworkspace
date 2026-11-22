"""Compares three ways to get a small icon from a large one.

The question this answers is the one the whole discussion turns on: is a baked atlas at a
generous resolution "just as good" as rasterising at the size actually drawn? The answer is
not a matter of taste — it can be measured.

  * direct      — the contour rasterised at 5 px (what the runtime path does);
  * atlas+mips  — a 256 px cell downsampled with a box filter (what a mipmapped DDS gives);
  * atlas-noMips— a 256 px cell sampled bilinearly with no mips (what a plain PNG may give).

The numbers are the share of the icon's ink that survives, and the roughness of the result.
"""

import os
import numpy as np
from PIL import Image

ROOT = r'e:\Projects\!VGS'
OUT = os.path.join(ROOT, r'tmp\icon-proto\out')
BIG = 256
SMALL = 5


def load(name, px):
    return np.array(Image.open(os.path.join(OUT, '%s-%d.pgm' % (name, px))), dtype=np.float32)


def bilinear_no_mips(image, size):
    """Samples a large mask at `size` points, 2x2 bilinear, no mip chain — GPU-like."""
    h, w = image.shape
    out = np.zeros((size, size), dtype=np.float32)
    scale = w / float(size)
    for y in range(size):
        for x in range(size):
            fx = (x + 0.5) * scale - 0.5
            fy = (y + 0.5) * scale - 0.5
            x0 = int(np.floor(fx)); y0 = int(np.floor(fy))
            tx = fx - x0; ty = fy - y0
            x0c = min(max(x0, 0), w - 1); x1c = min(max(x0 + 1, 0), w - 1)
            y0c = min(max(y0, 0), h - 1); y1c = min(max(y0 + 1, 0), h - 1)
            top = image[y0c, x0c] * (1 - tx) + image[y0c, x1c] * tx
            bottom = image[y1c, x0c] * (1 - tx) + image[y1c, x1c] * tx
            out[y, x] = top * (1 - ty) + bottom * ty
    return out


def ink(mask):
    return mask.sum() / 255.0


def roughness(mask):
    """Mean absolute difference between neighbouring pixels: high = jagged."""
    dx = np.abs(np.diff(mask, axis=1)).mean() if mask.shape[1] > 1 else 0.0
    dy = np.abs(np.diff(mask, axis=0)).mean() if mask.shape[0] > 1 else 0.0
    return (dx + dy) * 0.5


def main():
    print('%-22s %4s %-20s %7s %7s %9s' % ('icon', 'px', 'method', 'ink', 'rough', 'vs direct'))
    for name in ('circle-dot-solid', 'xmark-solid', 'square-plus-regular'):
        big = load(name, BIG)
        for size in (5, 9, 11, 15, 29):
            direct = load(name, size)
            box = np.array(Image.fromarray(big.astype(np.uint8)).resize(
                (size, size), Image.BOX), dtype=np.float32)
            near = bilinear_no_mips(big, size)

            base = ink(direct)
            rows = [
                ('direct %dpx' % size, direct),
                ('atlas %d->%d mips' % (BIG, size), box),
                ('atlas %d->%d no mips' % (BIG, size), near),
            ]
            for label, mask in rows:
                value = ink(mask)
                delta = 100.0 * (value - base) / base if base else 0.0
                print('%-22s %4d %-20s %7.1f %7.1f %8.1f%%' % (
                    name, size, label, value, roughness(mask), delta))
        print()


if __name__ == '__main__':
    main()
