# The map documents

Four files hold the two maps. Each map exists twice: once in KDL and once in XML. The two files of a pair must build the same layers, cell for cell.

| File | Mode | Size | Layers |
| --- | --- | --- | --- |
| `square-2d.kdl` | square 2D | 40 by 24 cells | `ground`, `decor` |
| `square-2d.xml` | square 2D | the same map | the same layers |
| `blocks-3d.kdl` | blocks 3D | 32 by 20 cells | `ground`, `decor` |
| `blocks-3d.xml` | blocks 3D | the same map | the same layers |

The app reads one file for each mode and syntax. The name comes from the mode and the syntax: `square-2d` or `blocks-3d`, then `.kdl` or `.xml`. Press Tab to swap the syntax, and 1 or 2 to swap the mode.

## Layers

Both maps declare two layers. A layer is a grid of its own, and the build hands back one grid for each layer, bottom first:

- `ground` holds the surface. The square map paints the field, the roads and the track, the broken ground, the plaza, the hill, and the snowfield as one cell per tile. The blocks map lays a mosaic of ground pieces, each one instance over the cells it covers.
- `decor` holds everything that stands on the surface: the woods, the shrubs and stones of the open field, the boulders, the hamlet, the thickets, the market and the orchard, the cache, the village, and the markers. In the blocks map it also holds the occasional raised block — a wall along the terrace edge, pillars, kerbs.

Paint written outside a layer would form a third layer named `main`, under both. These documents have none.

Two habits keep the split honest:

- A kernel paints every cell of its area, so a kernel belongs to one layer. The kernels in the square document are ground; the props — `bush`, `rockSmall`, `rockLarge`, `treeRound`, `treePine`, `pineSmall`, `snowTree`, `stones` — are elements a scatter places in `decor`.
- An element reports its rectangle, and the hover names the smallest one that covers a cell, so a prop buried under a building would still answer for it. The prop stands here sit in open ground, clear of every building and every route.

The split is what makes the scatters work: a scatter leaves the cells it did not use empty, and an empty cell in the decor grid draws nothing, so the ground shows through. The square 2D map paints 960 cells of ground and 481 cells of decor. The blocks map paints 160 ground instances and 126 decor instances, and the ground ones stand for all 640 cells of the map.

The blocks mode holds one column per cell, so the build lifts instead of folding: every layer above the ground rises by the height the layers below it reach at that cell, and the view draws each layer in turn. A decoration therefore stands on the surface rather than replacing it — the village floor sits on the terrace instead of sinking into the field.

## A spanned ground

The blocks map's ground is not one column per cell. It is a mosaic of the pieces the kit authors, each laid at the size it was drawn for: `block-grass-large` covers two cells by two, and its word states that span, so one `set` draws one instance over four cells.

| Region | Rectangle | Piece | Covers |
| --- | --- | --- | --- |
| the north woods floor | `x=0 y=0` 16 by 4 | `grassLowLarge` | 2 by 2, half a cell tall |
| the north-east drift | `x=16 y=0` 16 by 4 | `snowLowLarge` | 2 by 2, half a cell tall |
| the west field | `x=0 y=4` 12 by 12 | `grassLarge` | 2 by 2 |
| the village terrace | `x=12 y=4` 12 by 10 | `grassTall` | 2 by 2, two cells tall |
| the snow field | `x=24 y=4` 8 by 10 | `snowLarge` | 2 by 2 |
| the field below the terrace | `x=12 y=14` 12 by 2 | `grassLarge` | 2 by 2 |
| the south-east snow | `x=24 y=14` 8 by 6 | `snowLarge` | 2 by 2 |
| the south woods floor | `x=0 y=16` 12 by 4 | `grassLowLarge` | 2 by 2, half a cell tall |
| the south field | `x=12 y=16` 12 by 4 | `grassLarge` | 2 by 2 |

Each region is a run of piece nodes inside one flow container. The container declares its track pattern, the pieces carry no coordinates, and the pieces flow into the tracks in order:

```kdl
plot w=32 h=20 {
    cols auto auto auto auto auto auto auto auto auto auto auto auto auto auto auto auto

    // cells y=4..5: the west field, the village terrace, and the snow field
    grassLarge
    grassLarge
    grassLarge
    grassLarge
    grassLarge
    grassLarge
    grassTall
    ...
}
```

Sixteen tracks of two cells stand in a row, and a piece states its own size — `grassTall` covers two cells by two, and its element measures that from the span, so it fills one track. The container wraps when the row is full, so the ground is a list of pieces and nothing else. Each piece answers the hover under its own name.

The kit authors the ground at one, two, and four cells, and every piece carries its own bevel at its border. Placing a piece at the size it was drawn for keeps that bevel at the size it was drawn for, so pieces meet at their edges. Stretching a one-cell block over a whole region scales its bevel with it, which reads as a rim of empty space around a floating slab — the map's ground is a mosaic for that reason.

Heights come from the words too: the woods floors are half a cell below the fields, the fields are a cell, and the terrace is a two-cell piece whose top stands a step above everything around it. Nothing about the ground is decided per cell.

The second layer adds structures on top of it: the village on the terrace, a market plaza — one flat piece stretched over twenty cells, which is what a piece with no bevel is good for — a wall one block per cell, pillars, kerbs, the woods, and the props.

The same syntax works in any document. The framework guide is [Layers in authored maps](https://angelmunoz.github.io/Mibo/level-design/2d/layers.html), and [instances larger than a cell](https://angelmunoz.github.io/Mibo/level-design/3d/spans.html) states the span rules.

## Edit a map

1. Start the app from the repository root: `dotnet run --project LiveMap`.
2. Open one of the four files in a text editor.
3. Change a line, and save the file.

The app watches the file and rebuilds the map after the save. A document that builds replaces the map on the screen. A document that fails keeps the last good map, and the status line shows the error with its line and column.

The app reads the files in this folder and never writes to them.

## Check a map without a window

```bash
dotnet run --project LiveMap -- --check
```

The command builds each file with `DocFlow.buildLayers`, prints the size and the layers with the instances and the cells each one covers, and compares the two syntaxes of each pair layer by layer. It also checks that the ground is a mosaic of pieces over the whole map, that the decorations are lifted onto the piece they stand on, and fifteen cells against the layer that must answer for them and the region that must cover them, which is the same query the hover panel runs: the routes, the woods, the buildings, and the bare ground the decor layer left alone. The command exits with code 1 when a check fails.

## KDL and XML

The two syntaxes carry the same information. The resolver reads every value by name, so a document can move between the syntaxes without a change to the game.

| Item | KDL | XML |
| --- | --- | --- |
| The map and its size | `map 40 24 { ... }` | `<map w="40" h="24"> ... </map>` |
| A layer | `layer ground { ... }` | `<layer name="ground"> ... </layer>` |
| A statement | `fill grass` | `<fill cell="grass" />` |
| A piece that covers several cells | `set 0 0 grassLarge` | `<set x="0" y="0" cell="grassLarge" />` |
| A piece placed by flow | `grassLarge` | `<grassLarge />` |
| One instance over many cells | `set 13 11 slab spanX=10 spanZ=2` | `<set x="13" y="11" cell="slab" spanX="10" spanZ="2" />` |
| A rectangle | `fillRect 3 2 4 4 stone` | `<fillRect x="3" y="2" w="4" h="4" cell="stone" />` |
| A kernel over an area | `generate 0 6 40 2 forest` | `<generate x="0" y="6" w="40" h="2" kernel="forest" />` |
| An element with layout | `court x=2 y=2 w=6 h=4` | `<court x="2" y="2" w="6" h="4" />` |
| An element with a body | `element thicket { generate forest }` | `<element name="thicket"><generate kernel="forest" /></element>` |
| A style rule | `style thicket w=6 h=5` | `<style name="thicket" w="6" h="5" />` |
| A repeat | `repeat 6 { treeRound }` | `<repeat count="6"><treeRound /></repeat>` |

KDL reports the line and the column of an error. XML reports the name of the element, because the XML parser gives no positions. A layer carries one name and nothing else: another argument, another property, a duplicate name, an empty layer, or a layer outside the map all fail the build with their position.

## What the two maps contain

`square-2d` is a field with a wood along two edges, a road across the middle, broken ground in the north-east, a hamlet of a court, a camp and a yard, two thickets from a template the document declares, a market scatter, an orchard scatter, stands of shrubs and stones, boulders, and a plaza from a stamp.

`blocks-3d` is ground of two-cell pieces at three heights: woods floors and a snow drift half a cell below the fields, a village terrace a step above them, and fields of grass and snow between them. A village of a hut, a yard and a tower stands on the terrace behind a wall, with a market plaza in front of it; pillars and kerbs mark the way in; pines, snow trees, and loose stones fill the woods and the open field.
