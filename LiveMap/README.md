# LiveMap

LiveMap is a map editor. The map is a text file. You edit the file, save it, and the map on the screen is replaced.

Run the sample from the repository root:

```bash
dotnet run --project LiveMap
```

The app watches the document it shows, in `LiveMap/maps`. It never reads a copy from its own output folder, so the file you edit is the file it shows.

Two more commands:

```bash
# build every map in both syntaxes, print the result, and exit
dotnet run --project LiveMap -- --check

# read the maps from another folder
dotnet run --project LiveMap -- --maps /path/to/maps
```

## Keys

| Key | Action |
| --- | --- |
| Arrows, W A S D | move the view |
| Q and E | turn the 3D view |
| `+` and `-` | zoom in and out |
| Home | put the map in the middle of the view |
| 1 | square 2D map |
| 2 | blocks 3D map |
| Tab | swap the document syntax |
| F1 | show or hide the key list |
| F11 | fullscreen |
| Esc | quit |

No mouse button is bound. The pointer only selects the cell for the hover panel.

## The two maps

| Mode | Key | Grid | Drawing |
| --- | --- | --- | --- |
| square 2D | 1 | one `CellGrid2D<Cell>` for each layer | one sprite for each cell, one pass for each layer, bottom first |
| blocks 3D | 2 | one `CellGrid2D<BlockCell>` for each layer, lifted onto the layer below | one instanced draw for each instance: a column covers its cell, a plate covers its rectangle |

Each mode reads two files. The two files hold the same map in two syntaxes.

| Map | Files | Size | Layers |
| --- | --- | --- | --- |
| square 2D | `maps/square-2d.kdl`, `maps/square-2d.xml` | 40 by 24 cells | `ground`, `decor` |
| blocks 3D | `maps/blocks-3d.kdl`, `maps/blocks-3d.xml` | 32 by 20 cells | `ground`, `decor` |

## Write a map

This part is a tutorial. It starts from an empty document and adds one thing at a time.

### The map node

A document holds one `map` node. The node holds the width and the height in cells.

KDL:

```kdl
map 12 8 {
}
```

XML:

```xml
<map w="12" h="8">
</map>
```

Both files build the same grid. The rest of this tutorial writes KDL, and names the XML spelling where the two differ.

### Paint cells

A word paints one cell. `fill` paints the whole box with one word.

```kdl
map 12 8 {
    fill grass
}
```

`fillRect` paints a rectangle. It takes x, y, width, height, then the word.

```kdl
    fillRect 3 2 4 4 stone
```

XML spells the same statement with attributes: `<fillRect x="3" y="2" w="4" h="4" cell="stone" />`.

`set` paints one cell at exact coordinates.

```kdl
    set 5 5 coin
```

XML: `<set x="5" y="5" cell="coin" />`.

`border` outlines the box it is given. `rect` fills the box and then outlines it. `rect` takes two words: the edge, then the floor.

```kdl
    border crate
    rect stone stoneB
```

### Generate content from a rule

A kernel is a rule for one cell. `generate` calls the kernel for every cell of the area it covers. Add four numbers to state the area: x, y, width, height.

```kdl
    generate meadow
    generate 0 6 12 2 forest
```

A rule that reads only the cell coordinates gives the same map on every run. It also joins two generated areas with no seam.

A kernel that came from a stamp is a fixed picture instead. It paints at the origin of the area and repeats when the area is larger.

### Place an element

An element is a named piece: a shape, a set of words, or a stamp the game composed in F#. Elements take layout properties.

```kdl
    court x=2 y=2 w=6 h=4
    camp x=0 y=0
```

### Declare a template

A document declares its own elements with `element`. A `style` rule states layout once for every use of a name.

```kdl
    element thicket { generate forest }
    style thicket w=5 h=4
    thicket x=0 y=0
    thicket x=6 y=4
```

XML names both declarations with the `name` attribute: `<element name="thicket">` and `<style name="thicket" w="5" h="4" />`.

### Scatter and repeat

`pack=scatter` places the children at seeded, non-overlapping positions. `repeat` duplicates its children, so one written child becomes many scattered ones.

```kdl
    plot x=1 y=1 w=8 h=6 pack=scatter seed=3 {
        repeat 4 { treePine }
        crate
    }
```

### Split the map into layers

A map can declare layers. A layer is a grid of its own: the build hands back one grid for each layer, bottom first, and the app draws them in that order. An upper layer covers the one below it where it paints, and lets it show where it does not.

```kdl
map 12 8 {
    layer ground {
        generate meadow
        fillRect 0 5 12 1 way
    }

    layer decor {
        plot x=1 y=1 w=8 h=4 pack=scatter seed=3 { repeat 5 { treePine } }
    }
}
```

XML names the layer with the `name` property: `<layer name="ground"> ... </layer>`.

Four rules cover the construct:

- `layer` is legal only as a direct child of `map`.
- A layer carries one name and nothing else: a word argument in KDL, the `name` property in XML.
- Layer names are unique in the document, and a layer with no statements and no children fails the build.
- Paint written outside a layer — the map's own statements and its non-layer children — becomes a layer named `main`, under the stated ones.

Two habits keep a document's layers honest:

- **A kernel belongs to one layer.** A kernel paints every cell of its area, and there is no way to paint nothing, so the rules in `Semantics/Terrain.fs` and `Semantics/Volume.fs` are ground. The things that stand on that ground — shrubs, stones, boulders, trees, crates — are props: elements a `plot pack=scatter` places, which leaves the cells it did not use empty.
- **A prop goes where nothing covers it.** An element reports its rectangle, and the hover names the smallest one that covers the cell, so a prop buried under a building would still answer for it. The stands of props in these documents sit in open ground, clear of every building and every route.
- **An upper layer stands on the one below it.** The flat mode gets that from the sprite order; the blocks mode holds one column per cell, so the build lifts every layer above the ground by the height the layers below it reach at that cell. A decoration stands on the terrain, and the terrain stays whole underneath it. A plate lifts the whole rectangle it covers, so what stands over any of its cells lands on top of it.
- **One instance can cover many cells.** The kit authors the ground in pieces of one, two, and four cells, and a word states the size of the piece it names: `set 0 0 grassLarge` is four cells of ground drawn with one model. A statement may size a piece instead, with `spanX=` and `spanZ=`, which is how the market plaza stretches one flat piece over twenty cells. The cells a piece covers keep their values in the grid, the hover answers with the piece for every one of them, and the draw emits one instance per piece — so the blocks map's whole 640-cell ground leaves as 160 instances.
- **Place a piece at the size it was drawn for.** Every ground piece carries its own bevel at its border, and a span scales that bevel with it: stretching a one-cell block over a whole region leaves a rim of empty space around a floating slab. The map's ground is a mosaic of two-cell pieces for that reason, and its heights come from the words — half a cell for the woods floors, a cell for the fields, two cells for the terrace — so nothing about the ground is decided per cell.

Each layer reports its own element rectangles, so the hover names the region of the layer that painted the cell. The framework guide is [Layers in authored maps](https://angelmunoz.github.io/Mibo/level-design/2d/layers.html).

### Statements

| Statement | Effect |
| --- | --- |
| `fill WORD` | paints the whole box with the word |
| `fillRect X Y W H WORD` | paints a rectangle inside the box |
| `set X Y WORD` | paints one cell at a position inside the box |
| `border WORD` | outlines the box |
| `rect EDGE FLOOR` | fills the box, then outlines it |
| `generate [X Y W H] KERNEL` | runs the kernel over the box, or over the stated area |

### Layout properties

| Property | Meaning |
| --- | --- |
| `w=` `h=` | size in cells; absent means the element keeps its own size |
| `spanX=` `spanZ=` | how many cells one instance covers, on `set` only; absent means the word keeps the span it declares |
| `x=` `y=` | exact position inside the container |
| `col=` `row=` | grid slot that the child claims |
| `colspan=` `rowspan=` | how many tracks the slot spans |
| `area=` | named area of the grid template |
| `hplace=` `vplace=` `place=` | alignment: `start`, `center`, `end`, `stretch` |
| `pack=` | `stack` (default), `flow`, or `scatter` |
| `gapx=` `gapy=` | space between children; a flow grid takes one gap, so the two must match |
| `pad=` | inner margin |
| `seed=` | seed for `scatter` |

### Containers and declarations

| Node | Meaning |
| --- | --- |
| `map W H` | the root: one map, two dimensions |
| `layer NAME { ... }` | a layer: one grid of its own, drawn in document order |
| `plot` | anonymous box; place it with `x=` and `y=` |
| `grid` | container with declared tracks |
| `cols` `rows` | track sizes: a number, `fixed n`, or `auto` |
| `areas` | the grid template: one child per row, one name per column |
| `element NAME { ... }` | a template this document declares |
| `style NAME ...` | layout for every use of the name |
| `repeat N { ... }` | duplicates the children N times |

### What fails

A name outside the tables fails the build. KDL reports the line and the column; XML names the element. A document that fails keeps the last good map on the screen, and the status line shows the message.

## The vocabulary of the square 2D map

A document names three kinds of thing. The words are cells, the kernels are rules for one cell, and the elements are named pieces.

### Words

Each word paints one atlas tile and states whether it blocks movement. The hover panel shows both.

| Words | Meaning | Blocks |
| --- | --- | --- |
| `grass` `grassB` `grassC` | grass ground, three paints | no |
| `dirt` `dirtB` `dirtC` | dirt ground, three paints | no |
| `sand` `sandB` `sandC` | sand ground, three paints | no |
| `stone` `stoneB` `stoneC` | stone ground, three paints | no |
| `pathVerticalDirt` `pathHorizontalDirt` `pathEndUpDirt` `pathEndLeftDirt` | dirt road: straights and ends | no |
| `pathVerticalStone` `pathHorizontalStone` `pathEndUpStone` `pathEndLeftStone` | stone road: straights and ends | no |
| `bush` | small bush | no |
| `coin` | coin | no |
| `rockSmall` `rockMedium` `rockLarge` | three rock sizes | yes |
| `treeRound` `treePine` | round tree and pine | yes |
| `crate` `crateBeveled` | metal crates | yes |
| `container` `containerLarge` | shipping containers | yes |
| `turretMount` `turretBase` | turret mount and base | yes |
| `grassDotOnDirt` `sandDotOnDirt` `stoneDotOnDirt` `dirtDotOnGrass` `sandDotOnGrass` `stoneDotOnGrass` `grassDotOnSand` `dirtDotOnSand` `stoneDotOnSand` `grassDotOnStone` `dirtDotOnStone` `sandDotOnStone` | a speck of one material on another | no |
| `grassPatchOnDirt` `sandPatchOnDirt` `stonePatchOnDirt` `dirtPatchOnGrass` `sandPatchOnGrass` `stonePatchOnGrass` `grassPatchOnSand` `dirtPatchOnSand` `stonePatchOnSand` `grassPatchOnStone` `dirtPatchOnStone` `sandPatchOnStone` | a patch of one material on another | no |

### Kernels

| Kernel | Kind | Result |
| --- | --- | --- |
| `meadow` | rule | grass ground, in three paints |
| `forest` | rule | tree canopy in patches three cells wide |
| `gravel` | rule | broken ground: sand and gravel |
| `road` | rule | one worn road tile per cell, for a horizontal band |
| `track` | rule | the same, vertical |
| `camp` | stamp | the `Settlement.camp` stamp, five by five |
| `outpost` | stamp | the `Settlement.outpost` stamp, seven by seven |
| `plaza` | stamp | the `Settlement.plaza` stamp, seven by five |

A kernel paints every cell of its area, so a kernel belongs to one layer: the rules above are ground, and the document scatters the things that stand on it — `bush`, `rockSmall`, `rockLarge`, `treeRound`, `treePine` — in its decor layer.

### Elements

An area element paints a whole box. A prop element is one cell, so a scatter reads as a list of names.

| Element | Kind | Result |
| --- | --- | --- |
| `grove` | area | the `forest` kernel over the box |
| `meadow` | area | the `meadow` kernel over the box |
| `camp` | area, five by five | the `camp` kernel |
| `yard` | area | dirt inside a crate wall |
| `court` | area | stone edge, stone floor |
| `cache` | area | dirt with a coin at the centre |
| `bush` `rockSmall` `rockMedium` `rockLarge` `treeRound` `treePine` `crate` `crateBeveled` `container` `containerLarge` `turretMount` `turretBase` `coin` | prop | the word of the same name |

## The vocabulary of the blocks 3D map

A block map is a footprint. The vertical axis lives in the cell, so a word states how many cells tall its column stands. `wall` is one word for a column two and a half cells tall.

### Words

| Words | Meaning | Blocks |
| --- | --- | --- |
| `grass` `grassLow` | grass column, full and half height | yes |
| `grassEdge` `grassCorner` `grassNarrow` | grass pieces: edge, corner, narrow | yes |
| `snow` `snowLow` | snow column, full and half height | yes |
| `wall` | grass column, two and a half cells tall | yes |
| `pillar` | grass column, four cells tall | yes |
| `kerb` | narrow grass column, three quarters of a cell tall | yes |
| `grassLarge` `grassLowLarge` `grassTall` | ground pieces: two cells by two, a cell, half a cell, and two cells tall | yes |
| `grassLong` `grassLowLong` | ground pieces two cells by one | yes |
| `snowLarge` `snowLowLarge` `snowLong` | the same three sizes in snow | yes |
| `snowEdge` `snowCorner` | snow pieces: edge and corner | yes |
| `slab` | a plate: one model stretched over the cells the document states | no |
| `tree` `pine` `pineSmall` `snowTree` | planted trees | yes |
| `hedge` | hedge | yes |
| `stones` | loose stones | no |
| `crate` `crateStrong` `barrel` | containers | yes |
| `fence` | fence | yes |
| `pipe` | pipe | yes |
| `spike` | spike block | yes |
| `ladder` | ladder | no |
| `platform` | platform floor | no |
| `sign` `flag` | markers | no |
| `spring` | spring | no |
| `coin` | coin | no |

### Kernels

| Kernel | Kind | Result |
| --- | --- | --- |
| `field` | rule | ground at three heights, with the odd plant |
| `hill` | rule | columns that rise and fall on a sine of the row |
| `wood` | rule | a stand of trees over rolled ground |
| `snowfield` | rule | snow with drifts and bare rock |
| `hut` | stamp | the `Build.hut` stamp, three by three |
| `tower` | stamp | the `Build.tower` stamp, three by three |
| `yard` | stamp | the `Build.yard` stamp, five by five |
| `outpost` | stamp | the `Build.outpost` stamp, four by four |

### Elements

| Element | Kind | Result |
| --- | --- | --- |
| `field` | area | the `field` kernel over the box |
| `wood` | area | the `wood` kernel over the box |
| `hill` | area | the `hill` kernel over the box |
| `rampart` | area | ground inside a raised wall |
| `pit` | area | loose stones with a coin at the centre |
| `hut` | area, three by three | the `hut` kernel |
| `tree` `pine` `pineSmall` `snowTree` `hedge` `stones` `crate` `crateStrong` `barrel` `fence` `ladder` `flag` `coin` | prop | the word of the same name |

## Add a word, a kernel, or an element

The three tables live in `LiveMap/Semantics`. `Semantics/README.md` shows where each table is built and gives the code for a new entry.

## The hover panel

The pointer names what it is over:

- the cell coordinates,
- the layer that painted the cell,
- the word that painted the cell, or the model name in the blocks mode,
- whether that word blocks movement,
- the smallest region of the document that covers the cell,
- the instance that owns the cell, outlined in blue when one model covers more than that cell.

The map draws the cell and the region as outlines. The topmost layer that painted the cell answers, so the panel names what the reader sees. An element used four times reports four rectangles. The anonymous `plot` container reports under the name `plot`; ground a kernel painted sits in no region.

## Where the code lives

| Path | Contents |
| --- | --- |
| `LiveMap/maps` | the four documents |
| `LiveMap/Catalog` | the generated asset tables: the tile atlas and the model extents |
| `LiveMap/Semantics` | the vocabulary: words, kernels, stamps, and the two surfaces |
| `LiveMap/Document.fs` | the build: parse, resolve, emit, `Flow.runLayers` per layer, keep each layer's landmarks, lift the blocks layers onto the ground |
| `LiveMap/FileWatch.fs` | the watch: one debounced stream for each document |
| `LiveMap/Hover.fs` | the pointer query: the topmost layer, its word, and its region |
| `LiveMap/Check.fs` | the check behind `--check` |
| `LiveMap/Program.fs` | the router: dispatch, and translate events into commands |
