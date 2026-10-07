namespace Defli.MonoGame

open System.Numerics
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D
open Mibo.Layout
open Defli.State
open Defli.State.Systems

// ─────────────────────────────────────────────────────────────
// MapView — the map passes (terrain/road/decorations/base mount),
// restored from the original Defli module Map, reading the frame's
// static MapModel. The map never changes, so the view is a pure
// function of (textures, frame, culling rect).
// ─────────────────────────────────────────────────────────────

module MapView =

  /// Builds the native MonoGame atlas rectangle from a TileInfo's raw
  /// coordinates. The sim carries only the backend-neutral X/Y/Width/
  /// Height; the native rectangle is constructed here, at the view edge.
  /// Shared by all Defli.MonoGame views that draw atlas tiles.
  let inline tileRect(t: TileInfo) = Rectangle(t.X, t.Y, t.Width, t.Height)

  /// Picks the path tile frame for a cell from its path neighbors.
  /// The frame family follows the terrain the road crosses (the map
  /// keeps the zone's terrain on road cells), so a road over sand
  /// wears the sand-bordered sprites. Corners fall back to the
  /// vertical piece (placeholder — a nicer corner mapping can land
  /// later). No rotation is returned: the path frames are solid, and
  /// MonoGame's origin handling would shift the draw position.
  let private pathFrame
    (grid: CellGrid2D<MapTile>)
    (x: int)
    (y: int)
    : TileInfo =
    let isPath x y =
      grid |> CellGrid2D.get x y |> ValueOption.exists(fun t -> t.IsPath)

    let road =
      grid
      |> CellGrid2D.get x y
      |> ValueOption.map _.Terrain
      |> ValueOption.defaultValue TerrainKind.Grass
      |> MapModel.MapGround.road

    let n = isPath x (y - 1)
    let s = isPath x (y + 1)
    let e = isPath (x + 1) y
    let w = isPath (x - 1) y

    let count =
      (if n then 1 else 0)
      + (if s then 1 else 0)
      + (if e then 1 else 0)
      + if w then 1 else 0

    match count with
    | 1 ->
      // End piece — the frame's opening faces the road's continuation.
      if n then road.EndUp
      elif s then road.EndUp
      elif e then road.EndLeft
      else road.EndLeft
    | 2 when n && s -> road.Vertical
    | 2 when e && w -> road.Horizontal
    | _ -> road.Vertical // straight / corner placeholder

  /// `visible` is the camera's world-space view rect (camera bounds
  /// from CameraView.cullingBounds — iterVisible culls to it).
  let view
    (ctx: GameContext)
    (model: MapModel)
    (visible: Rectangle)
    (buffer: RenderBuffer2D)
    =
    let assets = GameContext.getService<IAssets> ctx
    let tex = assets.Texture Paths.Sheet
    let size = float32 Tiles.TileSize

    let terrain = MapModel.terrain model
    let pathGrid = MapModel.pathGrid model
    let waypoints = MapModel.waypoints model
    let decorations = MapModel.decorations model

    let left = int visible.X
    let top = int visible.Y
    let right = left + int visible.Width
    let bottom = top + int visible.Height

    // Terrain — the zone kinds (grass/sand/stone/dirt), only the
    // visible cells.
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y tile ->
        let pos = CellGrid2D.getWorldPos x y terrain
        let frame = MapModel.MapGround.frame x y tile.Terrain

        buffer
          .sprite(
            SpriteState.create(
              tex,
              Rectangle(int pos.X, int pos.Y, int size, int size),
              tileRect frame
            )
            |> SpriteState.withLayer Layers.Ground
          )
          .drop())
      terrain

    // Road — the carved cells with path-aware frames. NO origin/
    // rotation here: an origin of (32,32) would shift every tile half
    // a cell. The path frames are solid dirt — rotation is invisible
    // and omitted.
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y _ ->
        let frame = pathFrame pathGrid x y
        let pos = CellGrid2D.getWorldPos x y pathGrid

        buffer
          .sprite(
            SpriteState.create(
              tex,
              Rectangle(int pos.X, int pos.Y, int size, int size),
              tileRect frame
            )
            |> SpriteState.withLayer Layers.Path
          )
          .drop())
      pathGrid

    // Decorations — props + dirt blends (drawn over the road edge so
    // the blends merge into it). Culled like the other layers.
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y tile ->
        match tile.Decoration with
        | ValueSome frame ->
          let pos = CellGrid2D.getWorldPos x y decorations

          buffer
            .sprite(
              SpriteState.create(
                tex,
                Rectangle(int pos.X, int pos.Y, int size, int size),
                tileRect frame
              )
              |> SpriteState.withLayer Layers.Path
            )
            .drop()
        | ValueNone -> ())
      decorations

    // Base mount pad — from the waypoints layer (the base vertex).
    CellGrid2D.iterVisible
      left
      top
      right
      bottom
      (fun x y tile ->
        if tile.IsWaypoint && struct (x, y) = model.BaseCell then
          let pos = CellGrid2D.getWorldPos x y waypoints

          buffer
            .sprite(
              SpriteState.create(
                tex,
                Rectangle(int pos.X, int pos.Y, int size, int size),
                tileRect Tiles.turretMountEmpty
              )
              |> SpriteState.withLayer Layers.Path
            )
            .drop())
      waypoints
