# Icons used by the VGS editor

The three SVGs in this folder are copied verbatim from **Font Awesome Free 7.3.1**
(<https://fontawesome.com>), family `Free`, styles `Solid` and `Regular`.

| File | Icon | Style | Upstream path |
|---|---|---|---|
| `circle-dot-solid.svg` | `circle-dot` | Solid | `svgs/solid/circle-dot.svg` |
| `square-plus-regular.svg` | `square-plus` | Regular | `svgs/regular/square-plus.svg` |
| `xmark-solid.svg` | `xmark` | Solid | `svgs/solid/xmark.svg` |

## Licence

Icons: **CC BY 4.0** — <https://fontawesome.com/license/free>.
Fonts (not used here): SIL OFL 1.1. Code: MIT.

Attribution is required and is carried in the project `README.md` and in `metadata.mod`.
The files keep their upstream header comment, which states the same.

## Why the sources live in the repository

The Font Awesome package itself is not committed (`data/fontawesome-free-7.3.1-desktop` is
ignored), because it is a 30 MB download of 2883 icons of which three are used. Copying those
three here keeps the atlas reproducible from a clean checkout: no download, no package
manager, no network.

## Adding an icon

1. Copy the `.svg` into this folder (keep the upstream name: `<icon>-<style>.svg`).
2. Add one line to `../icons.txt`.
3. Run `pwsh -File tools/build-icon-atlas.ps1`.

Nothing in the mod needs editing: the build regenerates the enum, the atlas rectangles and the
SBC registration.
