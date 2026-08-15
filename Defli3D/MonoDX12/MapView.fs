namespace Defli3D.MonoGame

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Layout3D
open Defli3D
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// MapView — the terrain/road/decorations passes, baked once per map
// into two CellGrid3Ds (ground layer: terrain ∪ road ∪ markers;
// decorations layer: the props one step above the tile top) of
// precomputed (model name × world matrix) cells and drawn through
// the InstancedRenderContext parts overload: one instanced draw per
// distinct model per layer, zero-copy parts from ModelCache (the
// context folds each part's own absolute bone and passes the part's
// real buffer offsets). The map is static per State; a restart
// builds a new MapModel and re-bakes (identical content for the
// same config — the bake is pure data, no assets).
//
// The cell CONTENT (which model + rotation + offset) comes from the
// Shared MapModel.cellPieces — the single source of truth both
// backends' bakes consume; this view adds only the grid→CellGrid3D
// conversion and the native matrix math. The ground grid holds one
// model per cell (no overdraw): path cells → road piece by neighbor
// analysis (Models.roadTiles) with the spawn/base waypoint cells
// rendering the spawn tiles; other cells → the terrain tile for the
// cell's TerrainKind. The decorations grid is sparse — only cells
// whose MapTile carries a Decoration. The kit's models are
// bottom-anchored (origin at the footprint center on the ground
// plane, y = 0 = tile base — verified from the GLBs), so every
// transform is rotation × translation at (x + 0.5, yOffset, y + 0.5)
// — decorations sit at yOffset 0.2, the tile top.
// ─────────────────────────────────────────────────────────────

module MapView =

  /// System.Numerics vectors for the 3D grid/bounds (XNA Vector3 is
  /// the opened default here — no ambiguity).
  type private V3 = System.Numerics.Vector3

  /// One baked map cell: the content-model name (ModelInfo.Path —
  /// the mgcb asset name) + the precomputed local-to-world matrix.
  [<Struct>]
  type CellBake = { Name: string; Matrix: Matrix }

  /// The baked map: two CellGrid3D layers — the ground (terrain ∪
  /// path ∪ markers) and the decorations (sparse) — + the
  /// render-volume bounds.
  type MapBake = {
    Map: MapModel
    Ground: CellGrid3D<CellBake>
    Decorations: CellGrid3D<CellBake>
    Bounds: BoundingBox
  }

  /// Builds the two 3D grids from the 2D map layers. Pure data — no
  /// assets touched. The 2D (x, y) cell maps to 3D (x, 0, y) with the
  /// world position at the cell CENTER (+0.5). The content selection
  /// (model + rotation + offset) is the Shared MapModel.cellPieces:
  /// every cell gets its ground piece, decorated cells additionally
  /// a sparse cell on the decorations grid one layer above.
  let bake(map: MapModel) : MapBake =
    let terrain = MapModel.terrain map
    let w = terrain.Width
    let h = terrain.Height

    let ground = CellGrid3D.create w 1 h (V3(1f, 1f, 1f)) V3.Zero

    let decorations = CellGrid3D.create w 1 h (V3(1f, 1f, 1f)) V3.Zero

    for y = 0 to h - 1 do
      for x = 0 to w - 1 do
        let struct (groundPiece, decoPiece) = MapModel.cellPieces map x y

        let matrixOf(piece: MapModel.CellPiece) =
          let rotation =
            if piece.Rotation = 0f then
              Matrix.Identity
            else
              Matrix.CreateRotationY piece.Rotation

          let translation =
            Matrix.CreateTranslation(
              float32 x + 0.5f,
              piece.YOffset,
              float32 y + 0.5f
            )

          rotation * translation

        ground
        |> CellGrid3D.set x 0 y {
          Name = groundPiece.Model.Path
          Matrix = matrixOf groundPiece
        }

        decoPiece
        |> ValueOption.iter(fun piece ->
          decorations
          |> CellGrid3D.set x 0 y {
            Name = piece.Model.Path
            Matrix = matrixOf piece
          })

    {
      Map = map
      Ground = ground
      Decorations = decorations
      Bounds = {
        Min = V3.Zero
        Max = V3(float32 w, 1f, float32 h)
      }
    }

/// The map presenter: owns the lazily baked grids and the instanced
/// context — constructed once in Program.fs, no module-level mutable
/// state.
[<Sealed>]
type MapView() =

  /// The lazily baked grid for the current map (rebuilt on restart —
  /// the reference check is `obj.ReferenceEquals` on the MapModel).
  let mutable cachedBake: MapView.MapBake voption = ValueNone

  /// The instanced-render context over the baked grid: key = model
  /// name, parts = the zero-copy ModelCache parts (the context folds
  /// each part's own absolute bone and passes its real buffer
  /// offsets), transform = the precomputed cell matrix.
  let instancedCtx =
    InstancedRenderContext<MapView.CellBake, string>(
      getKey = (fun bake -> bake.Name),
      getParts = (fun bake -> ModelCache.resolve bake.Name),
      getTransform = (fun _ bake -> bake.Matrix)
    )

  /// The bake for the frame's map (re-baked only when the MapModel
  /// reference changes — a restart). Warms the part cache for every
  /// baked model so the first frame doesn't stall mid-draw.
  let ensureBake(frame: RenderFrame) : MapView.MapBake =
    match cachedBake with
    | ValueSome bake when obj.ReferenceEquals(bake.Map, frame.Map) -> bake
    | _ ->
      let bake = MapView.bake frame.Map

      ModelCache.warm(
        Array.append
          (bake.Ground.Cells
           |> Array.choose(fun c ->
             match c with
             | ValueSome cell -> Some cell.Name
             | ValueNone -> None))
          (bake.Decorations.Cells
           |> Array.choose(fun c ->
             match c with
             | ValueSome cell -> Some cell.Name
             | ValueNone -> None))
      )

      cachedBake <- ValueSome bake
      bake

  /// The map pass: ground + decorations, one instanced draw per
  /// distinct baked model per layer.
  member _.View(ctx: GameContext, frame: RenderFrame, buffer: RenderBuffer3D) =
    let bake = ensureBake frame
    instancedCtx.ResetFrameBuffers()

    CellGridRenderer3D.renderVolumeInstanced
      instancedCtx
      bake.Bounds
      bake.Ground
      buffer

    CellGridRenderer3D.renderVolumeInstanced
      instancedCtx
      bake.Bounds
      bake.Decorations
      buffer
