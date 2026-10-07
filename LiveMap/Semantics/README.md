# The vocabulary

This folder holds the names a document can use, and the F# pieces behind them. A name that is not in a table here fails the build of the document that uses it.

| File | Contents |
| --- | --- |
| `Bake.fs` | turns a Flow stamp into a kernel a document can name |
| `Noise.fs` | the hash that makes a rule repeatable |
| `Palette.fs` | the 57 words of the square 2D map |
| `Terrain.fs` | the rules of the square 2D map |
| `Settlement.fs` | the Flow stamps of the square 2D map |
| `Flat.fs` | the surface of the square 2D map: words, kernels, elements |
| `Blocks.fs` | the 28 words of the blocks 3D map |
| `Build.fs` | the Flow stamps of the blocks 3D map |
| `Volume.fs` | the surface of the blocks 3D map, and its rules |

## The three tables

A document resolves every name against one table. `Cell` and `BlockCell` are the values the tables hold.

| Table | A document writes | The value |
| --- | --- | --- |
| Words | `fill grass`, `set 3 4 treePine` | one cell: the tile and whether it blocks |
| Kernels | `generate forest` | a rule for one cell, `int -> int -> 'T` |
| Elements | `grove`, `court`, `crate` | a named piece, either a body of statements or one cell |

## Add a word

A word is a value with a name. Add the value in the palette and the name in the table.

1. Add the value next to the other values in `Palette.fs`:

```fsharp
/// A wooden floor. The tile is not in the sheet yet, so this example uses a
/// tile that is: the stone ground.
let plank = tile Tiles.stone false
```

2. Add the name to `Palette.words`:

```fsharp
  "plank", plank
```

3. Add the name to `LiveMap/Catalog/Tiles.curation` when the tile needs a short name of its own, and regenerate the table.

A document can now write `fill plank`. The hover panel reports `plank` because `Palette.wordOfTile` reads the same table.

To give a cell a second meaning, add a field to `Cell` in `LiveMap/Types.fs`. The words set it, the views read it, and the hover panel shows it. `Solid` is an example.

## Add a kernel

A kernel is a rule for one cell. Two kinds exist.

A rule reads only the cell coordinates, so it repeats with no seam and gives the same map on every run. `Noise.chance` gives a repeatable yes or no for a cell and a salt. Use a new salt for each rule.

```fsharp
/// A field of flowers: grass with a flower in about one cell in twelve.
let flowers: Doc.Kernel<Cell> =
  Doc.Gen2(fun x y ->
    if Noise.chance x y 61 8u then
      Palette.bush
    else
      Palette.grass)
```

A stamp is a picture. `Bake.kernel` runs a Flow stamp over a scratch grid and reads it back as a rule, so a document can name a piece the game composed. The width and the height are the size of the stamp, and the last value fills the cells the stamp left empty.

```fsharp
/// A watchtower, three cells square, that a document can name.
let tower: Stamp<Cell> =
  Stamp.box 3 3 [
    Flow.fill Palette.stone
    Flow.border Palette.stoneB
    Flow.cell { X = 1; Y = 1 } Palette.turretBase
  ]

let towerKernel = Bake.kernel 3 3 Palette.grass tower
```

Register the kernel in the surface:

```fsharp
  Kernels =
    frozen [
      "flowers", flowers
      "tower", towerKernel
    ]
```

A document can now write `generate flowers` and `generate tower`.

## Add an element

An element is a name a document writes as a node. Two helpers build one.

An area element fills the box the document gives it. Its body is a list of statements. The helper states no size, so the document states `w=` and `h=` at the use site, or a `style` rule states them once.

Add the entry to the `Elements` table of `Flat.fs` or `Volume.fs`, next to the other entries. The `element` and `prop` helpers are local to the surface file, so a new entry belongs in the same file.

```fsharp
  element "orchard" ValueNone [|
    Doc.Op.Fill Palette.grass
    Doc.Op.Border(ValueNone, Palette.treeRound)
  |]
```

A prop element is one cell. A scatter then reads as a list of names instead of a list of coordinates.

```fsharp
  prop "plank" plank
```

A document can now write `orchard x=4 y=4 w=6 h=5` and `plot pack=scatter { plank; plank; crate }`.

## The bridge between stamps and documents

The statement set of a document is closed: `fill`, `fillRect`, `set`, `border`, `rect` and `generate`. A Flow stamp is not a statement, so a game reaches a document in two ways.

1. Wrap the stamp in a kernel with `Bake.kernel`, and name it in the kernels table.
2. Build the same shape from statements, and name it in the elements table.

`Flat.fs` and `Volume.fs` show both. The plaza is a stamp, so it arrives as a kernel. The court is two statements, so it arrives as an element.

## Check a change

```bash
dotnet run --project LiveMap -- --check
```

The command builds both maps in both syntaxes. Add a name to a document that the tables do not hold, and the build fails with the line and the column.
