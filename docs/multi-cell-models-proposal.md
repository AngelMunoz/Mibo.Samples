---
title: Models Larger Than a Cell
category: Level Design
categoryindex: 8
index: 13
---

# Models larger than a cell

> **Status: decided — ready to implement. No open questions.** A span is
> vocabulary *and* syntax: the cell value states how many cells one instance
> covers, a document may state it too, and the build derives an `Occupancy`
> that every consumer resolves through. The renderer hands each instance its
> target box, so the game no longer re-derives the size. The vertical stack is
> derived as well. Flow changes nothing. Part 0 is the end-to-end tutorial;
> Parts 1-5 are the work, code first and documentation last.

## Why this exists

A block map holds one value per cell, so one cell is one instance. A 3D map
usually wants the opposite trade: author a model once, then scale it to fill
the space it occupies. One ground plate instead of 640 columns. A 4x4 deck. A
2x8 walkway.

Half of that already works. `getTransform` is the game's closure, and
Platformer3D already merges cells into rectangles and stretches one block
across each (`Platformer3D/Shared/WorldGen.fs`, `deriveSlabs`; the XZ scale in
`Platformer3D/Raylib/View.fs`). What breaks is everything else: the grid still
says one cell, so queries report empty ground, collision walks through the
plate, a window walk drops the instance when its anchor leaves the frame, and
nothing refuses two overlapping plates. Platformer3D answers that by keeping a
second grid as the source of truth and calling the slab grid a derived render
layer.

This plan makes the claim true in the grid, and hands the size to the draw
call. It is the 3D counterpart of what layer containers did for 2D.

### Parity with layers

| Layers (2D) | Instance spans (3D) |
| --- | --- |
| Declared in the document: `layer ground { }` | Declared in the document: `set 0 0 slab spanX=32 spanZ=20`, or in a word |
| The build returns it: `BuiltLayer[]` | The build returns it: `BuiltLayer.Occupancy` |
| Conflict rule: names are unique, a layer holds content | Conflict rule: a span stays in the grid, two spans do not overlap |
| One query for consumers: the topmost layer | One query for consumers: `Occupancy.owner` |
| Draw path: one pass per layer, in document order | Draw path: one instance per anchor, with the target box handed to the transform |
| Vertical stack: paint order gives transparency | Vertical stack: `Stack.feet`, derived from the layers below |
| Fails the build on misuse | Fails the build on misuse |

## The four numbers

Keep these apart. Most confusion in this area comes from mixing them.

| Number | Means | Lives in |
| --- | --- | --- |
| `spanX` `spanZ` | How many cells one instance covers in the grid plane | The cell value, and optionally the statement |
| `Height` | How tall the column stands, in cells | The cell value, set by the word |
| `w=` `h=` | How many cells an element paints | The element box |
| `SizeX` `SizeY` `SizeZ` | The mesh as authored | The model catalog |

There is no `spanY`. The grid is 2D: no cell sits above another, so a taller
column owns nothing. `spanY` would only stretch the mesh, which is what
`Height` already does. One number per concept.

## Decision

1. `Mibo.Core/Layout/Occupancy.fs` is a new file holding the span vocabulary,
   the `Occupancy` structure, and the stack derivation: `InstanceSpan` with
   `scan`, `owner`, `rectOf`, and `iterInWindow`, plus `Stack.feet` for the
   vertical. It sits beside `Landmarks` and answers two questions per cell:
   which instance owns it, and how high the layers below it reach.
2. `Doc.Surface` gains two fields: `Span`, which reads the span of a cell, and
   `WithSpan`, which writes a span a document states. The resolver welds
   element extents to spans, rejects area statements with spanning words, and
   checks that a stated size agrees with the span.
3. The document gains one spelling: `spanX=` and `spanZ=` on `set`. A word
   states the default span; a placement may override it. Front-ends do not
   change: both read scalars by name today.
4. Both 3D renderers gain a rect transform, an occupancy form of both
   whole-map and windowed draws, and the target box reaches the transform.
5. `Mibo.Layout.Flow` is untouched. So is the document syntax.
6. Three kinds of consumer are supported by design: code-first Flow games
   (`Occupancy.scan` with their own projection), document games
   (`DocFlow.buildLayers`), and games with their own pipeline.

## Part 0 — The map contract: an end-to-end walkthrough

This part is the tutorial. It states what a map is made of, what a cell holds,
and what the framework reads. Every later part assumes it.

### What a map is made of

**Word.** A cell value the game names. `Surface.Words` maps a string to a
`'T`, and a statement writes that value into a cell: `fill grass`,
`set 4 6 wall`. One word paints one cell.

**Kernel.** A rule for one cell, referenced by name through `generate`. The
type is one case today:

```fsharp
[<Struct>]
type Kernel<'T> = Gen2 of gen2: (int -> int -> 'T)
```

A kernel is a pure function of *local* cell coordinates. `Layout.generate`
clamps the requested area to the section and calls the function once per cell.
A kernel paints every cell of its area, and there is no way to paint nothing.
Two kinds are useful:

- **A rule:** a formula over the coordinates, so two generated areas join with
  no seam. LiveMap's `meadow`, `forest`, and `hill` are rules.
- **A baked stamp:** run a Flow `Stamp` once on a scratch grid, read the cells
  back, and repeat the picture over the area. LiveMap's `Bake.kernel width
  height empty stamp` does this, and `camp`, `outpost`, and `plaza` are stamps
  baked into kernels.

Kernel output is not knowable before the build runs, so a kernel that returns
a spanning cell is checked by `Occupancy.scan`, not by the resolver.

**Element.** A named body of statements, with an optional intrinsic size:

```fsharp
type ElementDecl<'T> = {
  Name: string
  Extent: CellSize voption
  Body: Op<'T>[]
}
```

The game declares some in `Surface.Elements`; a document declares its own with
`element name { ... }`. A document template that collides with a surface
element fails the build. Elements have no parameters: six corridor directions
are six declarations.

**Statement.** The closed union the resolver produces and the interpreter runs:

```fsharp
type Op<'T> =
  | Fill of 'T
  | FillRect of CellRect * 'T
  | Set of at: CellPoint voption * h: Align * v: Align * 'T
  | Border of area: CellRect voption * 'T
  | Rect of edge: 'T * floor: 'T
  | Generate of kernel: string * area: CellRect voption
```

Geometry is box-local: `FillRect` covers a local rect, `Set` places a local
point, `Generate` runs over a local rect, and an absent rect means the whole
box. Games extend through words, kernels, and element bodies, never through a
new case.

**Layer.** A document container holding one grid. The build returns one grid
per layer, bottom first, each with its own landmarks.

**Span.** How many cells one drawn instance covers. It lives in the cell value,
so a word carries it and a statement may override it.

**The built map.** What a consumer holds:

```fsharp
type BuiltLayer<'T> = {
  Name: string
  Grid: CellGrid2D<'T>
  Landmarks: Landmarks
  Occupancy: Occupancy      // new
}
```

For a stack of layers, `Stack.feet` adds the vertical: how high each layer
stands, from the layers below it.

### The cell type

The framework reads two things, and only through projections. Everything else
is the game's business.

| Field | Purpose | Who writes it | Read by |
| --- | --- | --- | --- |
| Model identity, or a `ModelInfo` that carries the authored size | Keys the draw and gives the transform its divisor | The word | The game: `getKey`, `getMeshesAndMaterial`, `getTransform` |
| `Height: float32` | Vertical stretch, in cells | The word | `Stack.feet` |
| `Span: InstanceSpan` | Grid-plane occupancy | The word, or the statement | `Occupancy.scan` |
| `Lift: float32` | Where the column stands | The build | The game: `getTransform` |
| `Solid: bool` | Gameplay meaning | The word | The game: collision, hover |

A 2D flat map needs no `Span` and no `Lift`: a sprite covers its cell. The
three projections a 3D map needs are one line each:

```fsharp
let spanOf (cell: BlockCell) = cell.Span
let heightOf (cell: BlockCell) = cell.Height
let withSpan (cell: BlockCell) (span: InstanceSpan) = { cell with Span = span }
```

### The map in a document: KDL

```kdl
map 20 12 {
    layer ground {
        fill grass
        set 4 6 slab spanX=6 spanZ=4      // one instance over 24 cells
    }

    layer decor {
        set 12 2 pillar
        set 14 2 pillar
        generate 0 10 20 2 field          // a kernel over an area
    }
}
```

### The same map in XML

```xml
<map w="20" h="12">
  <layer name="ground">
    <fill cell="grass" />
    <set x="4" y="6" cell="slab" spanX="6" spanZ="4" />
  </layer>
  <layer name="decor">
    <set x="12" y="2" cell="pillar" />
    <set x="14" y="2" cell="pillar" />
    <generate x="0" y="10" w="20" h="2" kernel="field" />
  </layer>
</map>
```

### The same map in Flow

Flow needs nothing new. The span is in the cell, and `Flow.cell` already places
a cell.

```fsharp
let map: Stamp<BlockCell> =
  Flow.box 20 12 [
    Flow.fill grass
    Flow.cell { X = 4; Y = 6 } { slab with Span = Span(across = 6, deep = 4) }
    Flow.cell { X = 12; Y = 2 } pillar
    Flow.cell { X = 14; Y = 2 } pillar
  ]
```

### The surface

```fsharp
let surface: Doc.Surface<BlockCell> = {
  Words = frozen [ "grass", grass; "slab", slab; "pillar", pillar; "tree", tree ]
  Kernels =
    frozen [
      "field", Gen2 field
      "hill", Gen2 hill
      "wood", Bake.kernel 6 6 grass wood
    ]
  Elements = frozen [ element "hut" hutBody; element "rampart" rampartBody ]
  Span = ValueSome spanOf        // absent: every cell is One
  WithSpan = ValueSome withSpan  // absent: no stated spans
}
```

### How a kernel ties to the map

1. A document or an element body holds `generate [X Y W H] NAME`.
2. The resolver turns it into `Op.Generate(NAME, area)`, and fails with the
   node's position when `NAME` is not in `Surface.Kernels`.
3. The emitter keeps the statement as data. The interpreter runs it against the
   section of the element that holds it, calling `Layout.generate`, which clamps
   the area to the section and calls the kernel once per cell.
4. Each returned cell lands in that layer's grid. A kernel that returns a
   spanning cell therefore places an anchor like any other paint.
5. `Occupancy.scan` then validates the layer. When a kernel makes two spans
   overlap, the build fails and names the layer and the cell.

That is the whole tie: a kernel is a name, a function of local coordinates, and
a write into the grid of the layer whose element ran it.

### Build

```fsharp
// a document, both syntaxes
match DocFlow.buildLayersXml(Volume.surface, source) with
| Ok layers -> layers          // one BuiltLayer per layer, each with an Occupancy
| Error reason -> failwith reason

// code-first, with Flow
let struct (grid, marks) = Flow.run map myGrid

let occupancy =
  Occupancy.scan spanOf grid
  |> Result.defaultWith failwith

// the vertical, for a stack of layers
let feet = Stack.feet [| occ0; occ1 |] [| grid0; grid1 |] heightOf
```

### Query

One query serves hover, collision, and spawns: which anchor owns this cell.

```fsharp
let ownerAt (layer: BuiltLayer<BlockCell>) (x: int) (y: int) =
  Occupancy.owner x y layer.Occupancy
  |> ValueOption.bind(fun at ->
    CellGrid2D.get at.X at.Y layer.Grid
    |> ValueOption.map(fun cell -> struct (at, cell)))
```

A covered cell answers with its anchor. A cell under a plate reports the plate,
not empty ground. `Occupancy.rectOf` gives the anchor's rect, for an outline.

### Render

Persist one context per map. The transform receives the target box, so the game
divides by the mesh and places the centre.

```fsharp
let context =
  InstancedRenderContext<BlockCell, string>.Rect(
    getKey = (fun cell -> cell.Model.Name),
    getMeshesAndMaterial = (fun cell -> meshesOf cell.Model),
    getTransform =
      fun (rect: CellRect) (basePos: Vector3) (cell: BlockCell) ->
        let boxW = float32 rect.W * cellSize
        let boxD = float32 rect.H * cellSize   // rect.H is depth
        let boxH = cell.Height * cellSize

        let scale =
          Matrix4x4.CreateScale(
            boxW / cell.Model.SizeX,
            boxH / cell.Model.SizeY,
            boxD / cell.Model.SizeZ)

        let place =
          Matrix4x4.CreateTranslation(
            basePos.X + boxW * 0.5f,
            basePos.Y + cell.Lift,
            basePos.Z + boxD * 0.5f)

        scale * place)
```

```fsharp
context.ResetFrameBuffers()

for layer in layers do
  context.RenderInstanced(buffer, layer.Grid, layer.Occupancy)
```

The windowed member takes the same arguments plus the four world bounds. It
converts them to cell space with `CellGrid2D.visibleRange` and enumerates
anchors by rect, so a plate stays drawn while any of its cells is in view.

### Stack

```fsharp
let feet = Stack.feet occupancies grids heightOf

for i in 0 .. grids.Length - 1 do
  let drawn =
    CellGrid2D.create w h cellSize Vector2.Zero

  CellGrid2D.iter
    (fun x y cell -> CellGrid2D.set x y { cell with Lift = feet[i][x + y * w] } drawn)
    grids[i]
```

`feet[i]` is the height the layers below layer `i` reach at each cell, flat at
`x + y * Width`. Heights add, because each layer is its own slab.

### Frame order

1. Read the source, or build the stamp in code.
2. Parse, resolve, emit, and paint: one grid per layer.
3. Scan each layer into an `Occupancy`.
4. Derive `Stack.feet` when more than one layer draws.
5. Cache the map. Nothing above runs per frame.
6. Per frame: reset the context buffers, compute the world window from the
   camera, and draw each layer through the occupancy form.
7. Queries run on demand, against the cached occupancy and grid.

## Rules, pinned

1. A span is stated in cells: `Span(across, deep)` on square grids — a
   rectangle of offset cells; `Radius r` on hex grids — a disc of hex steps;
   `One` — the identity, this cell only. `Span(1, 1)` and `Radius 0` are also
   the identity, so a computed span needs no special case.
2. `Span` needs both sides at least one. `Radius` needs `r >= 0`. Anything
   else fails the scan: `the span at (x,y) spans nothing`.
3. `Span` and `Radius` must match the grid geometry, and `One` is legal on
   both.
4. Only `set` places a spanning word. `fill`, `fillRect`, `border`, and `rect`
   with a spanning word fail with position:
   `the word 'slab' spans 6x4, so only set may place it`. Kernel output is not
   statically knowable; kernel-made spans stay to `scan`.
5. A statement states a span only when the surface exposes `WithSpan`.
   Otherwise: `the surface cannot state a span`. `spanX` and `spanZ` come as a
   pair, only on `set`, and only with its coordinate form.
6. A word states the default span, and a placement may override it. A stated
   box overrides a `Span` or `One` default; a `Radius` default rejects it:
   `the word 'hut' is a hex span, so a box cannot size it`.
7. The element extent is computed, not declared: `measureOps` consults the
   projection, so `element ground { set 0 0 slab spanX=32 spanZ=20 }` measures
   32x20 by itself. An extent cannot disagree with an instance because it is
   derived from it.
8. `Span(w, h)` contributes `(w, h)` to a measured extent; `Radius r`
   contributes `(2r+1, 2r+1)` — the offset-space bounding box of a hex disc,
   both orientations.
9. A style `w=`/`h=` and a declared `element ... w= h=` must both equal the
   derived extent, or the build fails with position naming the element and both
   sizes. Elements without spanning words stretch exactly as today.
10. A spanning anchor claims its rect. A plain cell under it is covered, not an
    error: the plate replaces the ground it covers, which is the point of the
    feature. `Occupancy.Claimed` counts the covered cells that held a value, so
    a check can assert the count.
11. `scan` fails, as a `Result` error, when a span leaves the grid
    (`the span at (x,y) covers past the grid edge`), when two spanning anchors
    overlap (`the span at (x,y) overlaps the span at (ax,ay)`), or when the
    shape and the geometry disagree (`Radius spans need a hex grid; (x,y) is
    square`, and the twin).
12. The rect is computed from the span, checked against the grid, and only then
    expanded. It is never derived from the expansion walk: the hex range walk
    clips to the grid, so a clipped walk cannot prove that a span left it.
13. A square anchor grows toward +X and +Z from its cell. A hex anchor is
    centred on its cell, and its rect is the offset-space bounding box
    `(x-r, y-r, 2r+1, 2r+1)` — the same convention Flow's hex landmarks use.
14. Cross-layer overlap is legal: layers stack, so a deck on a plate stands on
    it. Same-layer overlap between two spans is rule 11's error.
15. Nothing changes for a surface that states no projection: every document
    builds the same picture, and every existing golden passes.
16. `DocFlow.build` and `buildXml` cannot return an occupancy, so they fail on a
    document that places a spanning word, and name `buildLayers` in the message.
    They already fail on a multi-layer document.
17. A map with no spanning word keeps the old draw members. Every new sample
    uses the occupancy form, because it is the only form that sizes a span
    correctly on the whole-map path.

## Part 1 — `Mibo.Core/Layout/Occupancy.fs` (new file)

One file holds the three pieces, because they describe one thing: how a built
map is occupied and how its layers stack.

```fsharp
/// How many cells one drawn instance covers.
[<Struct>]
type InstanceSpan =
  /// Square grids: a rectangle of cells, growing toward +X and +Z from the
  /// anchor cell.
  | Span of across: int * deep: int
  /// Hex grids: a disc of hex steps, centred on the anchor cell.
  | Radius of r: int
  /// The identity: this cell only. The default for every word.
  | One

/// The occupancy a build derives from a painted grid.
type Occupancy = {
  Width: int
  Height: int
  /// anchor index -> its cell
  Cells: CellPoint[]
  /// anchor index -> its bounding rect, offset space
  Rects: CellRect[]
  /// cell -> anchor index + 1, flat at x + y * Width; 0 where nothing covers
  Owner: int[]
  /// covered cells that held a value and no longer draw
  Claimed: int
}

module Occupancy =
  /// Expands every populated cell through the projection, validates the
  /// result, and answers who owns a cell. `One` owns its own cell on any
  /// geometry, before the geometry dispatch. Result-based, like resolve
  /// errors.
  val scan :
    ('T -> InstanceSpan) -> CellGrid2D<'T> -> Result<Occupancy, string>

  /// An occupancy where every populated cell owns itself. For maps with no
  /// spans, and for tests.
  val identity : CellGrid2D<'T> -> Occupancy

  /// The anchor owning a cell: itself when populated and plain, the covering
  /// anchor when covered, ValueNone when empty. The one query every consumer
  /// goes through.
  val owner : int -> int -> Occupancy -> CellPoint voption

  /// An anchor's bounding rect.
  val rectOf : CellPoint -> Occupancy -> CellRect voption

  /// Enumerates anchors whose rect intersects a cell-space window.
  val iterInWindow :
    left: int -> top: int -> right: int -> bottom: int ->
    (CellPoint -> CellRect -> unit) -> Occupancy -> unit
```

Implementation pins:

- Two passes. Pass one expands every spanning anchor in row order, records its
  rect, bounds-checks it, and marks `Owner`. A cell already owned is rule 11's
  overlap error. Pass two adds the plain cells whose cell no span owns. A plain
  cell inside a span's rect is claimed: it never becomes an anchor.
- The anchor's own cell is an anchor. `Owner` for it holds its own index.
- `Claimed` counts covered cells that were populated and are not the anchor's own
  cell. A 32x20 plate over a fully painted ground reports 639.
- The hex disc comes from `Hex2DSpatial.Internal.forEachInRange`
  (`Spatial2D.fs:858`). It is `internal`, and this file is the same assembly. It
  clips to the grid, so use it for the expansion only, never for the rect or the
  bounds check. Orientation from `CellGrid2D.hexOrientation` (`Grid2D.fs:87`),
  which throws on a square grid, so dispatch on `Geometry` first.
- Three parallel arrays, no dictionary. `rectOf` resolves through `Owner`: the
  cell's index leads to the anchor, and the anchor leads to the rect.
- `iterInWindow` walks the anchor array and tests rect intersection. It is
  O(anchors), which is fine for maps up to a few thousand instances; add an
  index only when a sample proves the need.

Tests — `Mibo.Core.Tests/OccupancyTests.fs`:

- a square span expands to its rect, and every covered cell resolves to the
  anchor;
- a plain cell under a span is covered, and `Claimed` counts it;
- the anchor's own cell resolves to itself;
- two overlapping spans fail, and the message names both cells;
- a span past the edge fails and names the cell, at each of the four edges;
- `Span` on hex fails, `Radius` on square fails;
- a hex disc expands to a disc, and its rect is (2r+1)x(2r+1);
- `One` on both geometries owns one cell;
- `Span(0, 4)`, `Span(-1, 2)`, and `Radius -1` fail;
- empty cells resolve `ValueNone`, and an empty grid scans clean;
- `identity` owns every populated cell and claims nothing;
- `iterInWindow` finds an anchor whose rect intersects a window while its own
  cell is outside it, and skips one whose rect misses;
- a code-first consumer (a grid plus `Occupancy.scan`, no Markup) is exercised
  here.

### `Stack` — the vertical, in the same file

```fsharp
module Stack =
  /// For each layer, the height the layers below it reach at each cell, flat at
  /// x + y * Width. An upper layer writes this into its own cells, so a
  /// decoration stands on the stack instead of replacing it.
  ///
  /// `occupancies` and `grids` are parallel: entry i describes layer i, bottom
  /// first. A spanning anchor contributes its height over its whole rect.
  val feet :
    Occupancy[] -> CellGrid2D<'T>[] -> ('T -> float32) -> float32[][]
```

Implementation pins:

- `feet[0]` is all zeroes: the bottom layer stands on the plane.
- After recording `feet[i]`, advance the running top by each anchor's height: a
  plain anchor advances one cell, a spanning anchor advances its whole rect.
  Heights add, because each layer is its own slab.
- One allocation per layer for the returned arrays, at build time.
- A length mismatch with `grids` is an `invalidArg`, matching `Flow.runLayers`.

Tests — `Mibo.Core.Tests/StackTests.fs`:

- a decoration above a plate has the plate's height as its foot;
- three layers stack additively;
- a spanning anchor lifts its whole rect, not only its own cell;
- a layer above empty cells has a zero foot there;
- mismatched array lengths throw;
- the arrays are the grid's width times its height.

## Part 2 — `Mibo.Markup`

**`Doc.Surface` gains two fields:**

```fsharp
Span: ('T -> InstanceSpan) voption             // absent: every cell is One
WithSpan: ('T -> InstanceSpan -> 'T) voption   // absent: no stated spans
```

Both are breaking for every construction site. The exact list, five sites:
`LiveMap/Semantics/Flat.fs:53`, `LiveMap/Semantics/Volume.fs:94`,
`Mibo.Markup.Tests/DocTests.fs:18` and `:292`,
`Mibo.Markup.Tests/DocFlowTests.fs:25`. Three of them write the field as
`Doc.Words =`, so a grep for `{ Words =` misses them. Both fields are
`ValueNone` at every existing site, which is rule 15.

**Resolver (`Doc.fs`), all positioned:**

1. `resolveOp` (`Doc.fs:546`) is the single statement reader, and `takeCell`
   (`Doc.fs:580-588`) is the only `Words.TryGetValue`. Reject a spanning word
   on `fill`, `fillRect`, `border`, and `rect` there, with the node's position —
   rule 4. The three `fill`-family branches read the word at `Doc.fs:667`,
   `:669-674`, `:734-746`, and `:748-753`.
2. Read `spanX` and `spanZ` in the `set` branch only (`Doc.fs:772`), as a pair,
   and only for the coordinate form. Require `WithSpan`, and reject a stated box
   over a `Radius` default — rules 5 and 6. Write the span into the cell.
3. `measureOps` (`Doc.fs:829`) takes the surface, so it can read `Span`, and
   contributes span extents — rules 7 and 8. It is called from `paintOf`
   (`Doc.fs:864`), which already holds the surface.
4. One check function, called once per resolved child where the style and the
   element meet (`Doc.fs:1213-1218`, `Doc.fs:1329`), compares both size channels
   against the derived extent and fails naming the element and both sizes —
   rule 9. Today the two channels only pick a winner (`DocFlow.fs:70-75`,
   `DocFlow.fs:394-397`).
5. `validateSurface` (`Doc.fs:1456-1469`, called from `Doc.fs:1527`) extends its
   declared-body walk: an area op with a spanning cell in a game-declared
   element body fails, naming the element. This closes the F#-declared path;
   kernels stay to `scan`. Note the walk is at 1457-1469, not 1322-1339.

**`DocFlow.fs`:**

- `BuiltLayer<'T>` (`DocFlow.fs:556-560`) gains `Occupancy: Occupancy`, always
  present. There are two construction sites: `buildLayersFrom`
  (`DocFlow.fs:620-628`) and `LiveMap/Document.fs:154-163`.
- `buildLayersFrom` scans every layer after `Flow.runLayers` (`DocFlow.fs:618`),
  with the projection defaulting to `fun _ -> One`. A failure surfaces as
  `layer 'ground': <cell-named reason>`.
- `buildFrom` (`DocFlow.fs:650-686`) keeps its no-landmark promise and gains one
  allocation-free pass: walk the painted grid and fail if any cell carries a
  non-identity span, naming `buildLayers` — rule 16.

Tests — `Mibo.Markup.Tests/DocTests.fs` and `DocFlowTests.fs`, in the existing
lists:

- no projection: every existing golden passes;
- `fill slab` fails with position; `set` is legal;
- `spanX` without `spanZ` fails; `spanX` on `fill` fails; `spanX` with a surface
  that has no `WithSpan` fails; a stated box over a `Radius` default fails;
- a stated span overrides the word's default, and the derived extent follows the
  statement;
- a style `w=` that disagrees with the span fails naming both sizes; so does a
  declared `element ... w= h=`;
- `buildLayers` returns per-layer occupancies; a kernel-made span fails naming
  the layer and the cell;
- `build` fails on a document with a spanning word and names `buildLayers`;
- parity on a span document: KDL and XML produce the same grids and the same
  occupancy counts.

## Part 3 — both renderers

`Mibo.Raylib/Layout3D/Renderer3D.fs` and the MonoGame twin. Read the MonoGame
file first and confirm the context shape matches before mirroring.

**1. The rect transform.** `InstancedRenderContext` gains a static factory, not
a third constructor: three constructors whose arguments include function values
are ambiguous in F#, and the existing tests already pass named arguments to
separate two.

```fsharp
static member Rect :
  getKey: ('T -> 'K) *
  getMeshesAndMaterial: ('T -> struct (Mesh * Material3D)[]) *
  getTransform: (CellRect -> Vector3 -> 'T -> Matrix4x4)
  -> InstancedRenderContext<'T, 'K>
```

The factory follows the `SetPerMeshShaderResolver` precedent: it constructs
through the primary constructor and installs an internal rect transform.
`AddCell` takes a `CellRect` and calls the rect transform when one is installed,
and the two-argument transform with the cell's own rect `(x, y, 1, 1)` otherwise.
The two-argument form therefore never sees a span, which is rule 17.

**2. Four new draw members per backend**, each a thin wrapper over the shared
private emit path:

```fsharp
member RenderInstanced(buffer, grid, occupancy)
member RenderInstancedWithEffect(buffer, grid, occupancy, shaderForKey)
member RenderWindowInstanced(buffer, left, top, right, bottom, grid, occupancy)
member RenderWindowInstancedWithEffect(buffer, left, top, right, bottom, grid, occupancy, shaderForKey)
```

The whole-map pair is not optional: `RenderInstanced(buffer, grid)` walks
populated cells and would size a plate to one cell, because a span is a single
populated cell. The windowed pair enumerates `Occupancy.iterInWindow`, so a
plate stays drawn while any of its cells is in view. The keyed-effect twins exist
because Platformer3D's slabs use that member, and the review bot hunts backend
and API asymmetry.

**3. One shared window bound.** `CellGrid2D.visibleRange : left: int -> top: int
-> right: int -> bottom: int -> CellGrid2D<'T> -> struct (int * int * int *
int)` holds the world-to-cell math, geometry-aware and padded by one cell on
hex. `iterVisible` (`Grid2D.fs:166-224`) becomes a thin wrapper over it, and the
new windowed members call it before `iterInWindow`. `instancing.md` already
promises an orientation-aware window; the square-only math at
`Grid2D.fs:177-189` cannot keep that promise for a hex map.

**4. The class doc comment** changes from "one instance per populated cell" to
"one instance per anchor; an anchor receives its target box in `getTransform`,
so scale the model to it". It also states that a cell-windowed draw drops a
large instance when its anchor leaves the window, and that the occupancy form is
the fix.

**5. `Draw.fs` gains four overloads**, the occupancy forms of
`renderFootprintInstanced` and `renderFootprintWindowInstanced` for the plain and
the `shaderForKey` shapes (`Draw.fs:1516`, `:1528`, `:1548`, `:1566`). Each needs
its SRTP witness; the existing witness test pins the plain windowed form
(`Mibo.Raylib.Tests/Layout3DTests.fs:1121`).

Tests: an anchor outside the window whose rect intersects it is drawn; one whose
rect misses is not; the rect form hands the anchor's rect to the transform; the
whole-map occupancy form sizes a plate to its box. In
`Mibo.Raylib.Tests/Layout3DTests.fs` beside `twoCellFootprintGrid` (`:63`, used
at `:1097-1127`), and in the MonoGame twin — which has no footprint-grid helper
yet, so add it there first.

## Part 4 — LiveMap (validation, not design input)

The boundary first: nothing in Parts 1-3 names a sample. LiveMap converts as a
game with its own pipeline, and its steps validate the API.

**Cells and words**

- `Types.fs`: `BlockCell` gains `Span: InstanceSpan`. Both constructors
  (`BlockCell.ofModel`, `BlockCell.column`) default it to `One`.
- `Semantics/Blocks.fs`: one new word, `slab`, on the `platform` model: a thin
  plate, `Height = 0.25`, not solid, span `One` by default. A document states the
  size, so one word serves the ground plate and every deck.
- `Volume.surface`: `Span = ValueSome(fun cell -> cell.Span)` and
  `WithSpan = ValueSome(fun cell span -> { cell with Span = span })`.
- `Flat.surface`: both `ValueNone`.

**The maps**

The blocks map's ground is nine plates, and nothing else:

- `ground` (bottom): nine plates cover the whole map, each one instance over
  its own rectangle — `plot x=… w=… h=… { set 0 0 WORD spanX=… spanZ=… }`. The
  word decides the height and the material: `grass` and its edge, corner, and
  narrow siblings, `grassLow` at half a cell, `snow` and `snowLow`, and
  `shelf` at a cell and a half for the village terrace. Nothing about the
  ground is decided per cell, so the map reads as a surface with deliberate
  steps instead of a field of loose blocks.
- `decor`: the village on the terrace, a spanned wall along its edge, pillars
  and kerbs, the woods, the props, and the markers.

The square 2D map does not change. Both syntaxes state the same spans.

**The code**

- `Document.fs`: `buildDocument` fills its own `BuiltLayer` records
  (`:154-163`) with the scan result; a scan failure rides the existing `Result`
  path. `lift` (`:96-121`) is replaced by `Stack.feet` plus a write of
  `{ cell with Lift = foot }`.
- `Hover.fs`: `tryCellAt` (`:81-96`) resolves each layer through
  `Occupancy.owner` and reads the anchor's value. `Info.Cell` keeps the pointer
  cell, so the outline sits where the pointer is; the layer, the word, and the
  outline rect come from the anchor.
- `BlocksView.fs`: `transform` (`:29-47`) becomes the rect form and scales
  `rect.W * cellSize / Model.SizeX` by `rect.H * cellSize / Model.SizeZ`, with
  `Height * cellSize / Model.SizeY` on Y and the centre half the box along from
  the anchor cell. Note that `rect.H` is depth while `Height` is the vertical.
  `groundTop` (`:57-64`) resolves through the ground layer's occupancy, so an
  outline over the plate sits on the plate. The draw call at `:148` becomes the
  occupancy form.
- `Check.fs`: the probe table (`:13-30`) gains three entries — one cell over the
  plate expects `ground` and the `slab` word, one cell over the terrain expects
  `terrain`, one decor cell expects `decor`. `compareLayers` also compares the
  occupancy: anchor count and `Claimed`. Fix the maps README, which says
  "thirteen cells" for a table of twelve.
- The LiveMap README gains the span syntax in the statement table and the layer
  section, and the blocks word table gains `slab`.

## Part 5 — Documentation, written last

Documentation is written after the code lands. It describes what the code does,
with the shipped signatures, and gives guidance for real use. It is not a design
input, and it does not restate this plan. Part 0 is source material; the pages
are rewritten to match the code that landed.

The docs have two halves, and the infra half is the gap today:

- **Domain** — what the map is: words, kernels, elements, statements, layers,
  spans.
- **Infra** — what a game implements to consume a built map: the cell type, the
  surface tables, the build call, queries, the render context, the windowed
  path, the stack. Documented today as one paragraph (`markup.md` surface
  section) and otherwise only in sample code.

Pages:

- **New `docs/level-design/3d/infra.md` — the infra half.** The consumer
  walkthrough: declare, build, query, render, stack. State the two-halves framing
  in the first paragraph, and note that code-first games use
  `Flow.run`/`runLayers` plus `Occupancy.scan` with no Markup.
- **New `docs/level-design/3d/spans.md` — the domain half.** `InstanceSpan`, both
  geometries, the four numbers, the rules, the document spelling, the weld, and
  the error catalogue.
- **Fix `docs/level-design/3d/core.md`.** It is the obsolete-family page, and its
  "Multi-Cell Models" section (`:362-365`) says the opposite of rule 10: "Create
  stamps that fill all occupied cells for blocking/collision". Replace those two
  bullets with the span rule and a link to `spans.md`.
- `2d/markup.md`: the surface section gains both fields and links both pages.
- `2d/layers.md`: one line pointing at `spans.md` as the 3D counterpart.
- `graphics3d/instancing.md`: the per-anchor paragraph, the rect transform, and
  the windowed rule (`:200-212`).
- Front-matter for both new pages: `category: Level Design`, `categoryindex: 8`,
  `index: 13` and `14` (1-12 are taken). FsDocs orders pages by these values, and
  no TOC file exists.

## Non-goals

Rotation — it needs an orientation in the cell and a composing transform. Spans
read from mesh bounds — a mesh extent is not an occupancy; the word or the
statement states it. `spanY` — the grid is 2D and `Height` already covers the
vertical. Per-instance frustum bounds beyond window-rect intersection. Collision
policy — the occupancy answers who owns a cell; the game decides what that means.
2D flat-mode sprites larger than a cell — the same machinery, when a sample wants
it. A word parameterized by other data (`deck 2 2 4`) — `spanX`/`spanZ` covers
the sizes; other parameters stay deferred.

## Order of work

1. Core: `Occupancy.fs` added to `Mibo.Core.fsproj` after `Layout/Flow.fs` —
   `CellRect` and `CellPoint` live in `Flow.fs:9-14`, and `Spatial2D.fs` compiles
   earlier. Add `OccupancyTests.fs` to `Mibo.Core.Tests.fsproj`, whose file list
   is explicit. `dotnet test` green.
2. Markup: surface fields, resolver rules, `DocFlow`, tests, and the five surface
   sites.
3. Renderers: `visibleRange`, the rect factory, the eight draw members, the `Draw`
   overloads, tests on both backends.
4. LiveMap: cells, words, surfaces, maps, `Document.fs`, `Hover.fs`,
   `BlocksView.fs`, `Check.fs`, both READMEs; `--check` green.
5. `CHANGELOG.md`: one `Added` bullet under `[Unreleased]`, bold-prefixed by
   surface. `docs/migration-to-v6.md`: one entry for the two new `Surface` fields
   and the built-layer field.
6. Documentation: both new pages, the links, the `core.md` fix, the instancing
   paragraph, then `dotnet tool restore` and
   `dotnet fsdocs build --ignoreuncategorized` locally — CI builds the docs only
   after merge, so a broken page reaches the site otherwise.
7. `dotnet fantomas .` in both repos; framework PR first; the samples PR cites it
   and merges after.

## Acceptance checklist

- [ ] Every existing document and golden builds unchanged with no projection
      stated.
- [ ] `set 0 0 slab spanX=32 spanZ=20` draws one instance over 32x20 cells; the
      other 639 cells emit nothing, and `Claimed` reports 639.
- [ ] A plain cell under a span is covered, not an error. Two overlapping spans
      fail, and both cells are named.
- [ ] A span past the edge fails and names the cell, at every edge.
- [ ] `Span` on a hex grid fails; `Radius` on a square grid fails; `Span(0, 4)`
      and `Radius -1` fail.
- [ ] `fill slab` fails with position, in a document and in a declared element
      body. Only `set` places a spanning word.
- [ ] `spanX` without `spanZ`, `spanX` on `fill`, `spanX` on a surface with no
      `WithSpan`, and a stated box over a `Radius` default each fail with
      position.
- [ ] A stated span overrides the word's default, and the element's derived
      extent follows it. A disagreeing style `w=` or a disagreeing declared
      extent fails with both sizes named.
- [ ] `build` fails on a spanning document and names `buildLayers`.
- [ ] Hex: `Radius r` expands a disc, and its bounding box is (2r+1)x(2r+1) in
      offset space, matching the measured extent.
- [ ] The transform receives the anchor's rect, and an instance fills its box on
      X and Z while `Height` still drives Y.
- [ ] The whole-map occupancy form sizes a plate to its box; the old whole-map
      member still draws a map with no spans.
- [ ] A windowed render includes a rect-intersecting anchor whose own cell is
      outside the window, on both backends, for the plain and the keyed-effect
      members.
- [ ] `Stack.feet` puts a decoration on the plate beneath it, and a spanning
      anchor lifts its whole rect.
- [ ] The blocks map's ground draws nine instances over all 640 cells, and
      the village is lifted onto the terrace it stands on.
- [ ] `Mibo.Layout.Flow` is unchanged.
- [ ] The infra page walks a game from an empty project to a rendered, queryable,
      stacked map without reading sample code.
- [ ] `dotnet fsdocs build` succeeds locally with both new pages.
