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

Each map declares two layers. A layer is a grid of its own, and the build hands back one grid for each layer, bottom first:

- `ground` holds the terrain: the field, the roads and the track, the broken ground, the plaza, the hill, the snowfield.
- `decor` holds everything that stands on it: the woods, the shrubs and stones of the open field, the boulders, the hamlet, the thickets, the market and the orchard, the cache, the village, the rampart, the keep, the pillars, the markers.

Paint written outside a layer would form a third layer named `main`, under both. These documents have none.

Two habits keep the split honest:

- A kernel paints every cell of its area, so a kernel belongs to one layer. The kernels in these documents are ground; the props — `bush`, `rockSmall`, `rockLarge`, `treeRound`, `treePine`, `pineSmall`, `snowTree`, `stones` — are elements a scatter places in `decor`.
- An element reports its rectangle, and the hover names the smallest one that covers a cell, so a prop buried under a building would still answer for it. The prop stands here sit in open ground, clear of every building and every route.

The split is what makes the scatters work: a scatter leaves the cells it did not use empty, and an empty cell in the decor grid draws nothing, so the ground shows through. The square 2D map paints 960 cells of ground and 481 cells of decor; the blocks map paints 640 and 260.

The blocks mode holds one column per cell, so the build lifts instead of folding: every layer above the ground rises by the height the layers below it reach at that cell, and the view draws each layer in turn. A decoration therefore stands on the terrain rather than replacing it — the terrain stays whole, and the village floor sits on the field instead of sinking into it.

The same syntax works in any document. The framework guide is [Layers in authored maps](https://angelmunoz.github.io/Mibo/level-design/2d/layers.html).

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

The command builds each file with `DocFlow.buildLayers`, prints the size and the layers with the number of cells each one painted, and compares the two syntaxes of each pair layer by layer. It also checks thirteen cells against the layer that must answer for them and the region that must cover them, which is the same query the hover panel runs: the two routes, the woods, the buildings, and the bare ground the decor layer left alone. The command exits with code 1 when a check fails.

## KDL and XML

The two syntaxes carry the same information. The resolver reads every value by name, so a document can move between the syntaxes without a change to the game.

| Item | KDL | XML |
| --- | --- | --- |
| The map and its size | `map 40 24 { ... }` | `<map w="40" h="24"> ... </map>` |
| A layer | `layer ground { ... }` | `<layer name="ground"> ... </layer>` |
| A statement | `fill grass` | `<fill cell="grass" />` |
| A rectangle | `fillRect 3 2 4 4 stone` | `<fillRect x="3" y="2" w="4" h="4" cell="stone" />` |
| A kernel over an area | `generate 0 6 40 2 forest` | `<generate x="0" y="6" w="40" h="2" kernel="forest" />` |
| An element with layout | `court x=2 y=2 w=6 h=4` | `<court x="2" y="2" w="6" h="4" />` |
| An element with a body | `element thicket { generate forest }` | `<element name="thicket"><generate kernel="forest" /></element>` |
| A style rule | `style thicket w=6 h=5` | `<style name="thicket" w="6" h="5" />` |
| A repeat | `repeat 6 { treeRound }` | `<repeat count="6"><treeRound /></repeat>` |

KDL reports the line and the column of an error. XML reports the name of the element, because the XML parser gives no positions. A layer carries one name and nothing else: another argument, another property, a duplicate name, an empty layer, or a layer outside the map all fail the build with their position.

## What the two maps contain

`square-2d` is a field with a wood along two edges, a road across the middle, broken ground in the north-east, a hamlet of a court, a camp and a yard, two thickets from a template the document declares, a market scatter, an orchard scatter, stands of shrubs and stones, boulders, and a plaza from a stamp.

`blocks-3d` is rolling ground with a hill, woods along two edges, a snowfield with its snow trees, a village of a hut, a yard and a tower, a rampart, a keep, an avenue of pillars, stands of pines and loose stones, and three markers at a crossing.

