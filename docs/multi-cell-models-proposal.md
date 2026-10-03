---
title: Models Larger Than a Cell
category: Level Design
categoryindex: 8
index: 13
---

# Models larger than a cell

> **Status: proposal — open question, no decision.** The footprint grid emits
> one instance per cell, so a block map reads as voxels: every column is the
> same model, stretched only on Y. In 3D work the usual move is the opposite:
> author a model once and scale it on X, Y, and Z to fit — one large platform
> stretched over the whole ground, or a handful of platforms of different
> sizes that cover more than a cell each. This page states the problem, what
> the framework and the sample do today, and one suggestion for closing the
> gap. Nothing here is decided.

## The problem

A block map is a footprint grid: one cell holds one column, and the view draws
one instance for each populated cell. That model is why the LiveMap block map
looks voxelized — the terrain is 640 one-cell columns of `block-grass`, some
stretched to three cells on Y, and every decoration is a one-cell column on
top of them.

For a map that should read as built space rather than as blocks, an author
wants the other trade:

- one large platform model, scaled on XZ so a single instance covers the whole
  ground, with everything else placed on top of it;
- or a few platforms of different sizes, each covering more than one cell —
  a 4×4 deck, a 2×8 walkway — instead of 32 one-cell columns each.

The map's vocabulary cannot say either thing today. The document's unit is the
cell (`w=`/`h=` on an element is the box of cells that element paints, and a
kernel paints every cell of its area), and the renderer's unit is the cell
too. A document can therefore only ask for *more cells*, never for *one bigger
instance*.

## What each piece does today

- **`CellGrid2D<'T>`** holds one value per cell; `ValueNone` is empty. There
  is no occupancy, anchor, or footprint concept.
- **`InstancedRenderContext<'T, 'K>`** walks the grid, groups populated cells
  by key, and emits one instanced draw per key. One cell is one instance.
- **`getTransform: Vector3 -> 'T -> Matrix4x4`** is entirely the game's. The
  framework never inspects the matrix. LiveMap normalizes each column to
  exactly one cell in XZ (`cellSize / Model.SizeX`) and stretches it on Y
  (`Height / Model.SizeY`), which is already a scale-on-an-axis rule — the
  same lever that XZ scaling needs.
- **`Mibo.Markup` / `Flow`** address cells. `Op.Generate(kernel, area)` calls
  the kernel for every cell of the area, and a `Stamp`'s `W`/`H` are in cells.
- **Queries read the grid.** The hover picks the topmost populated cell and
  looks its rectangle up in that layer's `Landmarks`; collision would read the
  same cells.

## The suggestion

Keep the grid as the addressing model, and make the footprint part of the
cell's own data, with three small rules.

1. **A cell may state a footprint** in cells, next to the height it already
   states: `Footprint = struct (4f, 4f)` for a deck, `struct (40f, 24f)` for a
   slab that covers the ground. A word the game declares carries it, so the
   document only names the word.
2. **The transform scales XZ by the footprint**, exactly as it scales Y by the
   height:

   ```fsharp
   let scaleX = Constants.cellSize * fst cell.Footprint / cell.Model.SizeX
   let scaleZ = Constants.cellSize * snd cell.Footprint / cell.Model.SizeZ
   ```

3. **The game marks the covered cells once, at load.** A pass over the built
   grid marks every cell inside a footprint as covered, and
   `getMeshesAndMaterial` returns an empty array for a covered cell, so no
   second instance is emitted there. This is the same kind of load-time pass
   the sample already runs for the layer lift.

The document then says `set 16 10 deck`, or reserves the room with a plot:

```kdl
layer ground {
    set 0 0 bedrock          // one instance, scaled over the whole map
}

layer decor {
    set 4 6 deck             // a 4x4 platform, one instance
    plot x=4 y=6 w=4 h=4 { ... }   // optional: documents the space it takes
}
```

Two consequences come with the rules, and both stay the game's business:

- **Occupancy bookkeeping.** The grid still holds one value per cell, so the
  covered cells need a marker. Nothing in `CellGrid2D` will do it for you.
- **Queries.** The hover, a spawn query, or a collision test that reads the
  grid must resolve a covered cell to the anchor that owns it — carried in the
  same load-time pass, not recomputed per frame.

What the framework could add later, if this proves ergonomic: nothing in the
renderer, and nothing in `Flow`/markup beyond what is proposed here. A
footprint is rendering metadata; the paint statements stay cell-granular.

## The alternative: leave the grid cell-sized

Two shapes need no new machinery at all, and both are legitimate:

- **Author the model at the size you want.** A 4×4 platform GLB drawn as one
  cell keeps one instance per cell and keeps the grid the truth. The cost is a
  model per size, which is the trade the current vocabulary makes.
- **Keep large geometry out of the grid.** `RenderBuffer3D` draws meshes
  directly, so a map can draw its ground as a handful of scaled platforms and
  keep the footprint grid for the cell-sized things standing on it. The grid is
  not mandatory; it is one way to address the world.

## Rules, if the suggestion is adopted

1. A footprint is stated in cells, in XZ, next to `Height` in Y. A cell with
   no stated footprint is 1×1, so every existing word keeps its meaning.
2. The instance is anchored at its cell's near corner and grows towards +X and
   +Z. A footprint that runs past the grid's edge fails the load instead of
   clamping silently.
3. A covered cell emits nothing and answers as its anchor when a query reads
   it.
4. A covered cell written onto by a later layer is an authoring error: two
   instances cannot share one footprint.
5. The lift composes: a cell's footprint does not change the height the layers
   above it are lifted by; the lift still reads `Height`.
6. Nothing in the document changes for a map that states no footprint.

## Acceptance checks

- [ ] A one-cell word still draws exactly as it does today; every existing
      document builds the same picture.
- [ ] A 4×4 footprint draws one instance, and the 15 covered cells emit
      nothing.
- [ ] A footprint that covers the map draws one instance, scaled on XZ.
- [ ] The hover over a covered cell names the layer and region of its anchor.
- [ ] A covered cell that a later layer paints fails the load with its
      position.
- [ ] A footprint past the grid's edge fails the load, naming the cell.

## Non-goals

- **Automatic footprints from mesh bounds.** A model's mesh extent is not its
  footprint: `block-grass` is 1.082 cells wide so neighbouring cubes overlap,
  and a tree's mesh is narrower than the cell it stands in. The word states the
  footprint.
- **Rotation.** The suggestion is axis-aligned scaling only; a rotated instance
  needs an orientation in the cell and a transform that composes it, which is a
  separate question.
- **Culling and bounds.** The framework has no per-instance bounds today, so a
  slab covering the map is never culled. That is acceptable for a few large
  instances and would need its own work for many.
- **Flow or markup emitting instances.** A document names words; the game owns
  what a word means, including how big one instance of it is.
