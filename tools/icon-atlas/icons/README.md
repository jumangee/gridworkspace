# Icons used by the VGS editor

The SVGs in this folder are copied verbatim from **Font Awesome Free 7.3.1**
(<https://fontawesome.com>), family `Free`, styles `Solid` and `Regular`.

| File | Icon | Style | Upstream path |
|---|---|---|---|
| `circle-dot-solid.svg` | `circle-dot` | Solid | `svgs/solid/circle-dot.svg` |
| `square-plus-regular.svg` | `square-plus` | Regular | `svgs/regular/square-plus.svg` |
| `xmark-solid.svg` | `xmark` | Solid | `svgs/solid/xmark.svg` |
| `bell-regular.svg` | `bell` | Regular | `svgs/regular/bell.svg` |
| `lightbulb-solid.svg` | `lightbulb` | Solid | `svgs/solid/lightbulb.svg` |
| `lightbulb-regular.svg` | `lightbulb` | Regular | `svgs/regular/lightbulb.svg` |
| `share-from-square-regular.svg` | `share-from-square` | Regular | `svgs/regular/share-from-square.svg` |

> The jump-to-terminal button was asked for as `up-right-from-square` **Regular**. That icon has no
> Regular cut in the free set — `metadata/icons.json` lists `styles: ["solid"]` for it, and
> `svgs/regular/up-right-from-square.svg` does not exist. `share-from-square` (alias
> `share-square`) is the same square-with-an-arrow-leaving-it silhouette and *does* ship in Regular,
> so it is the one copied. The reasoning is recorded in
> [ADR-0021](../../docs/adr/0021-jump-to-block-terminal.md) §Context 4.

Which icon is drawn by which control is recorded in the catalogue,
[`docs/07-icon-catalog.md`](../../docs/07-icon-catalog.md). This file only says where the source
files came from.

## Licence

Icons: **CC BY 4.0** — <https://fontawesome.com/license/free>.
Fonts (not used here): SIL OFL 1.1. Code: MIT.

Attribution is required and is carried in the project `README.md` and in `metadata.mod`.
The files keep their upstream header comment, which states the same.

## Why the sources live in the repository

The Font Awesome package itself is not committed (`data/fontawesome-free-7.3.1-desktop` is
ignored), because it is a 30 MB download of 2883 icons of which seven are used. Copying those
seven here keeps the atlas reproducible from a clean checkout: no download, no package manager,
no network.

## Adding an icon

1. Copy the `.svg` into this folder (keep the upstream name: `<icon>-<style>.svg`).
2. Add one line to `../icons.txt`.
3. Add a row to [`docs/07-icon-catalog.md`](../../docs/07-icon-catalog.md) — the catalogue is
   checked against the manifest by `tools/check-icon-catalog.ps1`.
4. Run `pwsh -File tools/build-icon-atlas.ps1`.

The build regenerates the enum, the material table, the SBC registration and the catalogue
previews in `docs/icons/`; the mod code only needs editing where the new icon is drawn. To see
the card's button row or the port controls at real sizes,
`python tools/icon-atlas/card_row_preview.py` and `python tools/icon-atlas/controls_reference.py`
redraw them from the same constants and textures.
