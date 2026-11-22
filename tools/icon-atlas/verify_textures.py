"""Independent check of the shipped icon textures against the generated table and the SBC.

"Independent" is the point: this does not import the builder, it re-reads the PNG files with
Pillow and re-parses the two generated artefacts. A bug in the builder that also produced a
matching artefact would be invisible to a self-check.

The icons are PNG, not DDS. The DDS version decoded correctly here and at every mip level and
still drew as an opaque square in the game's sprite path; PNG was the fix. ADR-0019 §1 records
the experiment table.

Run: python tools/icon-atlas/verify_textures.py
"""

import os
import re
import sys

from PIL import Image

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
TEXTURE_DIR = os.path.join(ROOT, r'src\VGS.Mod\Textures\VGS\Icons')
CODE = os.path.join(ROOT, r'src\VGS.Mod\Data\Scripts\VGS\Ui\VgsIconAtlas.g.cs')
SBC = os.path.join(ROOT, r'src\VGS.Mod\Data\GuiTextures.sbc')

# The ink bounding box, as a fraction of the texture side. Every glyph is scaled to fill the
# inset box, so the box is the same for all three: (256 - 2 * 8) / 256 = 0.9375. Checking it
# catches an inset change or a glyph that no longer fills its box.
EXPECTED_INK_RATIO = {
    'CircleDot': 0.9375,
    'SquarePlus': 0.9375,
    'Xmark': 0.9375,
}


def parse_table():
    text = open(CODE, encoding='utf-8').read()
    size = int(re.search(r'TextureSize = (\d+)', text).group(1))
    count = int(re.search(r'Count = (\d+)', text).group(1))
    names = re.findall(r'return "(VGS_\w+)";', text)
    scale = re.search(r'TextureScale\b.*?return ([0-9.]+)f', text, re.S)
    return size, count, names, scale.group(1) if scale else '?'


def parse_sbc():
    text = open(SBC, encoding='utf-8').read()
    return dict(re.findall(r'<SubtypeName>(VGS_\w+)</SubtypeName>\s*<Path>([^<]+)</Path>', text))


def ink_ratio(image):
    """Fraction of the side covered by the ink's bounding box, from the alpha channel."""
    alpha = image.getchannel('A')
    box = alpha.getbbox()
    if box is None:
        return 0.0
    return (box[2] - box[0]) / float(image.size[0])


def main():
    size, count, names, texture_scale = parse_table()
    sbc = parse_sbc()

    problems = []
    print('table: %d icon(s), texture size %d, TextureScale %s' % (count, size, texture_scale))
    print()

    if len(names) != count:
        problems.append('table declares %d icon(s) but names %d' % (count, len(names)))

    for name in names:
        icon = name[4:] if name.startswith('VGS_') else name
        path = os.path.join(TEXTURE_DIR, icon + '.png')
        expected_path = 'Textures\\VGS\\Icons\\%s.png' % icon

        if name not in sbc:
            problems.append('%s is not registered in GuiTextures.sbc' % name)
            continue
        if sbc[name] != expected_path:
            problems.append('%s points at %s, expected %s' % (name, sbc[name], expected_path))
            continue
        if not os.path.exists(path):
            problems.append('%s is missing on disk' % path)
            continue

        image = Image.open(path)
        image.load()
        rgba = image.convert('RGBA')

        if image.size != (size, size):
            problems.append('%s is %dx%d, the table says %d' % (icon, image.size[0], image.size[1], size))

        # Straight alpha over white: every pixel's RGB must be 255, or the sprite shader would
        # multiply a tinted colour by alpha and darken the edges twice.
        r, g, b, a = rgba.split()
        if min(r.getextrema()) != 255 or min(g.getextrema()) != 255 or min(b.getextrema()) != 255:
            problems.append('%s has non-white RGB; alpha must be straight over white' % icon)

        if a.getextrema()[0] != 0:
            problems.append('%s has no fully transparent pixel; its corners must be clear' % icon)

        ratio = ink_ratio(rgba)
        expected = EXPECTED_INK_RATIO.get(icon)
        if expected is not None and abs(ratio - expected) > 0.02:
            problems.append('%s ink box is %.3f of the side, expected ~%.3f' % (icon, ratio, expected))

        status = 'OK'
        print('  %-12s %dx%d RGBA  ink %.3f of side  %s' % (icon, image.size[0], image.size[1], ratio, status))

    # A PNG nothing references is a file the mod ships for no reason.
    for file in os.listdir(TEXTURE_DIR):
        if not file.lower().endswith('.png'):
            continue
        name = 'VGS_' + os.path.splitext(file)[0]
        if name not in names:
            problems.append('%s is shipped but no icon references it' % file)

    # The DDS route was abandoned; a leftover would be shipped and never loaded.
    for file in os.listdir(TEXTURE_DIR):
        if file.lower().endswith('.dds'):
            problems.append('%s is a DDS, but the icons ship as PNG (ADR-0019 §1)' % file)

    print()
    if problems:
        print('TEXTURES FAILED — %d problem(s)' % len(problems))
        for problem in problems:
            print('  ' + problem)
        return 1

    print('TEXTURES OK — %d texture(s) agree with the generated table and the SBC' % len(names))
    return 0


if __name__ == '__main__':
    sys.exit(main())
