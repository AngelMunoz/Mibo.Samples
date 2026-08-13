namespace Defli3D.Raylib

#nowarn "9"

open System
open System.Collections.Generic
open System.Numerics
open FSharp.NativeInterop
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics3D
open Mibo.Layout3D
open Raylib_cs
open Defli3D.State
open Defli3D.State.Frame
open Defli3D.State.Systems

// ─────────────────────────────────────────────────────────────
// Shared draw-side helpers (this file compiles before the other
// views — Enemies/Towers/Projectiles/Vfx/World reuse them).
//   Layers       — the HUD render layer for the 2D pass (3D has no
//                  layer concept; this only orders the HUD).
//   ModelMeshes  — per-model mesh/material cache: every view resolves
//                  a ModelInfo to its sub-mesh (Mesh * Material3D)[]
//                  ONCE, then reuses the cached arrays every frame
//                  (Platformer3D's meshMaterialCache pattern). The
//                  per-frame GameContext is set once per frame
//                  (Platformer3D's currentGameContext recipe — the
//                  InstancedRenderContext resolver takes none).
//   InstanceScratch — grow-only per-model-name instance-transform
//                  arrays for the entity views (ModelProbe idiom):
//                  the views reset → fill → draw per frame; the
//                  buffer copies transforms into pooled arrays at
//                  record time, so reusing one scratch per kind per
//                  frame is safe. NOT a batcher — plain per-kind
//                  arrays, one .instanced draw per (name × sub-mesh).
// ─────────────────────────────────────────────────────────────

/// The HUD render layer (Defli's Layers module reduced to what the
/// 3D frontend uses — the world pass is layer-less).
module Layers =

  [<Literal>]
  let Hud = 10<Mibo.Elmish.Graphics2D.RenderLayer>

/// Per-model (Mesh * Material3D)[] cache, keyed by ModelInfo.Name.
/// Resolves through IAssets once per model ("assets/{Path}.glb" —
/// ModelInfo.Path is extensionless), then reuses the arrays.
module ModelMeshes =

  let private cache = Dictionary<string, struct (Mesh * Material3D)[]>()

  /// The per-frame GameContext used for lazy asset loads (the
  /// InstancedRenderContext resolver doesn't receive one — Platformer3D's
  /// currentGameContext recipe). Set at the top of the world pass.
  let mutable private currentContext: GameContext voption = ValueNone

  let setContext(ctx: GameContext) : unit = currentContext <- ValueSome ctx

  /// The model's sub-meshes with their authored materials converted
  /// to Material3D (the Platformer3D resolveMeshesAndMaterial recipe).
  /// Loaded once per model name and cached for the process lifetime.
  let resolve(info: ModelInfo) : struct (Mesh * Material3D)[] =
    match cache |> Dictionary.tryGetValue info.Name with
    | ValueSome cached -> cached
    | ValueNone ->
      let ctx =
        match currentContext with
        | ValueSome c -> c
        | ValueNone ->
          failwith
            $"ModelMeshes.resolve called before the first frame ({info.Name})"

      let assets = GameContext.getService<IAssets> ctx
      let m = assets.Model($"{info.Path}.glb")

      let result =
        if m.MeshCount > 0 then
          [|
            for mi = 0 to m.MeshCount - 1 do
              let mesh = NativePtr.get m.Meshes mi
              let matIdx = NativePtr.get m.MeshMaterial mi
              let raylibMat: Material = NativePtr.get m.Materials matIdx

              let material3d: Material3D =
                Material3D.fromRaylibMaterial raylibMat

              struct (mesh, material3d)
          |]
        else
          Array.empty

      cache[info.Name] <- result
      result

  /// Same as resolve, keyed by model NAME (the views' batch keys).
  /// Unknown names resolve to an empty mesh array — no crash on a
  /// bad key, the group just draws nothing.
  let inline resolveByName(name: string) : struct (Mesh * Material3D)[] =
    match Models.tryByName name with
    | ValueSome info -> resolve info
    | ValueNone -> Array.empty

  /// Warms the cache for every name (avoids mid-frame Content.Load
  /// stalls when a model first appears).
  let inline warm(names: string[]) : unit =
    for name in names do
      resolveByName name |> ignore

/// Grow-only per-model-name instance-transform scratch for the
/// entity views (ModelProbe idiom — one Matrix4x4[] per model kind,
/// refilled every frame; steady state allocates nothing). NOT a
/// batcher: the views own the per-frame fill and the draw timing;
/// this module only owns the arrays. Each view resets → fills →
/// draws at its own point in the pass (a view's reset clears every
/// group, so the groups of views that already drew are gone). The
/// render buffer copies the transforms into pooled arrays at record
/// time, so refilling one scratch per kind per frame is safe.
module InstanceScratch =

  let private transforms = Dictionary<string, Matrix4x4[]>()
  let private counts = Dictionary<string, int>()

  /// Clears every group's count (arrays keep their storage).
  let reset() : unit = counts.Clear()

  /// Appends one instance transform to the name's group (grows the
  /// scratch array on the cold path).
  let add (name: string) (transform: Matrix4x4) : unit =
    match counts |> Dictionary.tryGetValue name with
    | ValueSome n ->
      let arr = transforms[name]

      if arr.Length <= n then
        let bigger = Array.zeroCreate<Matrix4x4>(max (arr.Length * 2) 32)
        Array.Copy(arr, bigger, n)
        transforms[name] <- bigger

      transforms[name][n] <- transform
      counts[name] <- n + 1
    | ValueNone ->
      let arr = Array.zeroCreate<Matrix4x4> 32
      arr[0] <- transform
      transforms[name] <- arr
      counts[name] <- 1

  /// One .instanced draw per (name × sub-mesh): `resolve` maps a name
  /// to its cached meshes/materials (ModelMeshes).
  let draw(buffer: RenderBuffer3D) : unit =
    for KeyValueV(name, n) in counts do
      if n > 0 then
        let meshes = ModelMeshes.resolveByName name

        for mi = 0 to meshes.Length - 1 do
          let struct (mesh, material) = meshes[mi]

          buffer.instanced(mesh, transforms[name], material, n).drop()

// ─────────────────────────────────────────────────────────────
// MapView — the static world (terrain/road/spawn-base/decorations)
// from the frame's MapModel, baked once per map into two CellGrid3Ds
// (ground layer: terrain ∪ road ∪ markers; decorations layer: the
// props one step above the tile top) of precomputed (model name ×
// world matrix) cells and drawn through the Platformer3D
// InstancedRenderContext recipe: one instanced draw per distinct
// model. The map is static per State; a restart builds a new
// MapModel and re-bakes (identical content for the same config —
// the bake is pure data, no assets).
//
// The cell CONTENT (which model + rotation + offset) comes from the
// Shared MapModel.cellPieces — the single source of truth both
// backends' bakes consume; this view adds only the grid→CellGrid3D
// conversion and the native matrix math. The ground grid holds one
// model per cell (no overdraw, no layering epsilons); the
// decorations grid is sparse — only cells whose MapTile carries a
// Decoration. The kit's models are bottom-anchored (origin at the
// footprint center on the ground plane, y = 0 = tile base — verified
// from the GLBs), so every transform is rotation × translation at
// (x + 0.5, yOffset, y + 0.5) — decorations sit at yOffset 0.2, the
// tile top.
// ─────────────────────────────────────────────────────────────

module MapView =

  /// One baked map cell: the content-model name (ModelInfo.Name —
  /// the ModelMeshes key) + the precomputed local-to-world matrix.
  [<Struct>]
  type CellBake = { Name: string; Matrix: Matrix4x4 }

  /// The baked map: two CellGrid3D layers — the ground (terrain ∪
  /// path ∪ markers) and the decorations (sparse) — + the
  /// render-volume bounds.
  type MapBake = {
    Map: MapModel
    Ground: CellGrid3D<CellBake>
    Decorations: CellGrid3D<CellBake>
    Bounds: Mibo.Layout3D.BoundingBox
  }

  /// The lazily baked grid for the current map (rebuilt on restart —
  /// the reference check is `obj.ReferenceEquals` on the MapModel).
  let mutable private cachedBake: MapBake voption = ValueNone

  /// Builds the two 3D grids from the 2D map layers. Pure data — no
  /// assets touched. The 2D (x, y) cell maps to 3D (x, 0, y) with the
  /// world position at the cell CENTER (+0.5). The content selection
  /// (model + rotation + offset) is the Shared MapModel.cellPieces:
  /// every cell gets its ground piece, decorated cells additionally
  /// a sparse cell on the decorations grid one layer above.
  let private bake(map: MapModel) : MapBake =
    let terrain = MapModel.terrain map
    let w = terrain.Width
    let h = terrain.Height

    let ground = CellGrid3D.create w 1 h (Vector3(1f, 1f, 1f)) Vector3.Zero

    let decorations = CellGrid3D.create w 1 h (Vector3(1f, 1f, 1f)) Vector3.Zero

    for y = 0 to h - 1 do
      for x = 0 to w - 1 do
        let struct (groundPiece, decoPiece) = MapModel.cellPieces map x y

        let matrixOf(piece: MapModel.CellPiece) =
          let rotation =
            if piece.Rotation = 0f then
              Raymath.MatrixIdentity()
            else
              Raymath.MatrixRotateY piece.Rotation

          let translation =
            Raymath.MatrixTranslate(
              float32 x + 0.5f,
              piece.YOffset,
              float32 y + 0.5f
            )

          Raymath.MatrixMultiply(rotation, translation)

        ground
        |> CellGrid3D.set x 0 y {
          Name = groundPiece.Model.Name
          Matrix = matrixOf groundPiece
        }

        match decoPiece with
        | ValueSome piece ->
          decorations
          |> CellGrid3D.set x 0 y {
            Name = piece.Model.Name
            Matrix = matrixOf piece
          }
        | ValueNone -> ()

    {
      Map = map
      Ground = ground
      Decorations = decorations
      Bounds = {
        Min = Vector3.Zero
        Max = Vector3(float32 w, 1f, float32 h)
      }
    }

  /// The bake for the frame's map (re-baked only when the MapModel
  /// reference changes — a restart). Warms the mesh/material cache
  /// for every baked model so the first frame doesn't stall mid-draw.
  let private ensureBake(frame: RenderFrame) : MapBake =
    match cachedBake with
    | ValueSome bake when obj.ReferenceEquals(bake.Map, frame.Map) -> bake
    | _ ->
      let bake = bake frame.Map

      ModelMeshes.warm(
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

  /// The instanced-render context over the baked grid (Platformer3D
  /// recipe): key = model name, transform = the precomputed cell
  /// matrix.
  let private instancedCtx =
    InstancedRenderContext<CellBake, string>(
      getKey = (fun bake -> bake.Name),
      getMeshesAndMaterial = (fun bake -> ModelMeshes.resolveByName bake.Name),
      getTransform = (fun _ bake -> bake.Matrix)
    )

  /// The map pass: ground + decorations, one instanced draw per
  /// distinct baked model per layer.
  let view (ctx: GameContext) (frame: RenderFrame) (buffer: RenderBuffer3D) =
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
