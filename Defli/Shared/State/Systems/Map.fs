namespace Defli.State.Systems

open System.Collections.Generic
open System.Numerics
open Mibo.Layout
open Defli.State

// ─────────────────────────────────────────────────────────────
// Map sub-system — owns a LayeredMap<MapTile> (one parallel
// CellGrid2D per concern) and the path. Static content (built once
// at state init, never mutated — same rule as Kimo's map/stores;
// NOT adaptive).
//
// Layers (MapLayers):
//   Terrain    — the zone fills (grass/sand/stone/dirt)
//   Path       — the road, stamped over the waypoint segments
//   Buildable  — build permission; road and obstacle props clear it
//   Waypoints  — the path's vertex cells (spawn/base markers)
//   Decorations— props and terrain blends
//
// Level-1 ("Old Harbour") is authored as ONE Flow document
// (Mibo.Layout.Flow): zones as grid-template areas, props as
// clumps, a docked dockside depot. The road's waypoints are NOT
// hand-coordinated — they derive from the zone rectangles the
// layout resolved (Flow.tryPosition), so trading a track weight
// re-flows the road with the zones. The document grid is then
// split into the layers above. Level-2 stays procedural (findPath
// + floodFill) and keeps the Layout stamp machinery.
// The view iterates with CellGrid2D.iterVisible over the camera's
// world-space view rect — culled to the visible cells even though
// the fixed screen currently shows the whole grid.
// ─────────────────────────────────────────────────────────────

/// Layer indices of the map's parallel grids.
[<RequireQualifiedAccess>]
module MapLayers =
  [<Literal>]
  let Terrain = 0

  [<Literal>]
  let Path = 1

  [<Literal>]
  let Buildable = 2

  [<Literal>]
  let Waypoints = 3

  [<Literal>]
  let Decorations = 4

/// The map's parallel concern layers — a dictionary of grids the
/// sample owns. This is exactly what the retired framework
/// `LayeredGrid2D` was: dims plus a lazily filled layer dictionary.
type LayeredMap<'T> = {
  Width: int
  Height: int
  CellSize: Vector2
  Origin: Vector2
  Layers: Dictionary<int, CellGrid2D<'T>>
}

module LayeredMap =

  let create
    width
    height
    (cellSize: Vector2)
    (origin: Vector2)
    : LayeredMap<'T> =
    {
      Width = width
      Height = height
      CellSize = cellSize
      Origin = origin
      Layers = Dictionary()
    }

  let getOrAddLayer
    index
    (m: LayeredMap<'T>)
    : struct (CellGrid2D<'T> * LayeredMap<'T>) =
    let mutable existing = Unchecked.defaultof<CellGrid2D<'T>>

    if m.Layers.TryGetValue(index, &existing) then
      struct (existing, m)
    else
      let grid = CellGrid2D.create m.Width m.Height m.CellSize m.Origin
      m.Layers.Add(index, grid)
      struct (grid, m)

  /// Runs a `Layout` paint pipeline over one layer (the retired
  /// `LayeredMap.runLayer`).
  let runLayer
    index
    (paint: GridSection2D<'T> -> GridSection2D<'T>)
    (m: LayeredMap<'T>)
    : LayeredMap<'T> =
    let struct (grid, m) = getOrAddLayer index m
    Layout.run paint grid |> ignore
    m

type MapModel = {
  Grid: LayeredMap<MapTile>
  /// World-space waypoint centers (spawn → base) — the movement
  /// (physics) phase walks these.
  Path: Vector2[]
  SpawnCell: struct (int * int)
  BaseCell: struct (int * int)
}

module MapModel =

  let private grassTile = {
    Terrain = TerrainKind.Grass
    IsPath = false
    Buildable = true
    IsWaypoint = false
    Decoration = ValueNone
  }

  let private pathTile = {
    Terrain = TerrainKind.Dirt
    IsPath = true
    Buildable = false
    IsWaypoint = false
    Decoration = ValueNone
  }

  let private nonBuildableTile = { grassTile with Buildable = false }

  /// A decorations-layer row: the sprite frame to draw over the
  /// terrain (dirt blends keep Buildable = true — ground paint;
  /// props on procedural maps set Buildable = false — obstacles).
  let inline private decoTile(frame: TileInfo) = {
    grassTile with
        Decoration = ValueSome frame
  }

  let inline private obstacleTile(frame: TileInfo) = {
    decoTile frame with
        Buildable = false
  }

  /// A layer's CellGrid2D (all layers exist after create).
  let inline layer (index: int) (m: MapModel) : CellGrid2D<MapTile> =
    let struct (grid, _) = LayeredMap.getOrAddLayer index m.Grid
    grid

  let inline terrain(m: MapModel) = layer MapLayers.Terrain m
  let inline pathGrid(m: MapModel) = layer MapLayers.Path m
  let inline buildableGrid(m: MapModel) = layer MapLayers.Buildable m
  let inline waypoints(m: MapModel) = layer MapLayers.Waypoints m
  let inline decorations(m: MapModel) = layer MapLayers.Decorations m

  /// A cell is buildable iff its Buildable layer row carries Buildable
  /// (the road stamp overwrote the cells under it).
  let inline isBuildable (x: int) (y: int) (m: MapModel) : bool =
    m |> buildableGrid |> CellGrid2D.get x y |> ValueOption.exists _.Buildable

  // ── Level-1 "Old Harbour" — a Flow-authored level document ──

  /// Ground frame families per terrain kind — the sim carries only
  /// TerrainKind; both views pick atlas frames through this module.
  /// All tables are precomputed: the view calls are per-frame hot
  /// path and must not allocate.
  module MapGround =

    let kindName =
      function
      | TerrainKind.Grass -> "grass"
      | TerrainKind.Dirt -> "dirt"
      | TerrainKind.Stone -> "stone"
      | TerrainKind.Sand -> "sand"

    let private stoneFulls = [|
      Tiles.byName["stone_full_a"]
      Tiles.byName["stone_full_b"]
      Tiles.byName["stone_full_c"]
    |]

    let private sandFulls = [|
      Tiles.byName["sand_full_a"]
      Tiles.byName["sand_full_b"]
      Tiles.byName["sand_full_c"]
    |]

    let private fulls =
      function
      | TerrainKind.Grass -> Tiles.groundGrass
      | TerrainKind.Dirt -> Tiles.groundDirt
      | TerrainKind.Stone -> stoneFulls
      | TerrainKind.Sand -> sandFulls

    let private textured =
      function
      | TerrainKind.Grass -> Tiles.byName["grass_full_textured"]
      | TerrainKind.Dirt -> Tiles.byName["dirt_full_textured"]
      | TerrainKind.Stone -> Tiles.byName["stone_full_textured"]
      | TerrainKind.Sand -> Tiles.byName["sand_full_textured"]

    /// Deterministic ground frame for a cell — three plain variants
    /// plus the textured one on every fifth cell.
    let frame (x: int) (y: int) (kind: TerrainKind) : TileInfo =
      let v = (x * 7 + y * 13) % 5

      if v = 4 then textured kind else (fulls kind)[v % 3]

    /// Road piece frames for one terrain kind — the road sprites
    /// carry the terrain they cross (path_vertical_sand and so on).
    [<Struct>]
    type RoadFrames = {
      Vertical: TileInfo
      Horizontal: TileInfo
      EndUp: TileInfo
      EndLeft: TileInfo
    }

    let private roadTable =
      [|
        TerrainKind.Grass
        TerrainKind.Dirt
        TerrainKind.Stone
        TerrainKind.Sand
      |]
      |> Array.map(fun kind ->
        let n = kindName kind

        {
          Vertical = Tiles.byName[$"path_vertical_{n}"]
          Horizontal = Tiles.byName[$"path_horizontal_{n}"]
          EndUp = Tiles.byName[$"path_end_up_{n}"]
          EndLeft = Tiles.byName[$"path_end_left_{n}"]
        })

    let private kindIndex =
      function
      | TerrainKind.Grass -> 0
      | TerrainKind.Dirt -> 1
      | TerrainKind.Stone -> 2
      | TerrainKind.Sand -> 3

    let road(kind: TerrainKind) : RoadFrames = roadTable[kindIndex kind]

  let private zoneTile(kind: TerrainKind) = { grassTile with Terrain = kind }

  let private obstacle kind frame = {
    zoneTile kind with
        Buildable = false
        Decoration = ValueSome frame
  }

  let private dressing kind frame = {
    zoneTile kind with
        Decoration = ValueSome frame
  }

  /// One zone: a named, context-sized canvas — terrain fill plus
  /// clumps of props. Each `(frame, blocks, count)` triple scatters
  /// `count` copies of `frame`; `blocks` decides obstacle vs visual
  /// dressing.
  let private zone
    (name: string)
    (kind: TerrainKind)
    (seed: int)
    (props: (TileInfo * bool * int) list)
    : Stamp<MapTile> =
    let styles = [
      Flow.fill(zoneTile kind)

      for i, (frame, blocks, count) in List.indexed props do
        Flow.clumps { Count = count; Seed = seed + i } (fun s ->
          Flow.cell
            { X = 0; Y = 0 }
            ((if blocks then obstacle else dressing) kind frame)
            s

          s)
    ]

    Stamp.named name (Flow.canvas styles)

  /// Carve one road segment into the document grid, keeping the
  /// zone's terrain under the road (the road sprites are
  /// terrain-aware, so Layout.repeatX's single-content fill cannot
  /// express this — the per-cell read is the whole point).
  let private carveSegment
    (doc: CellGrid2D<MapTile>)
    (struct (px, py): struct (int * int))
    (struct (tx, ty): struct (int * int))
    =
    if py = ty then
      for x in min px tx .. max px tx do
        match CellGrid2D.get x py doc with
        | ValueSome t ->
          CellGrid2D.set x py { pathTile with Terrain = t.Terrain } doc
        | ValueNone -> ()
    else
      for y in min py ty .. max py ty do
        match CellGrid2D.get px y doc with
        | ValueSome t ->
          CellGrid2D.set px y { pathTile with Terrain = t.Terrain } doc
        | ValueNone -> ()

  /// Terrain-blend pass — patches at every zone boundary: a cell
  /// next to a different terrain gets the matching
  /// `<other>_patch_on_<mine>_<side>` frame (dressing, never
  /// blocks). Road cells skip — the road sprites border themselves.
  /// First direction wins, deterministically (N, S, W, E).
  let private blendZones(doc: CellGrid2D<MapTile>) : unit =
    let sides = [|
      struct (0, -1, "top")
      struct (0, 1, "bottom")
      struct (-1, 0, "left")
      struct (1, 0, "right")
    |]

    CellGrid2D.iter
      (fun x y tile ->
        if not tile.IsPath && tile.Decoration.IsNone then
          let mutable frame = ValueNone

          for struct (dx, dy, side) in sides do
            match CellGrid2D.get (x + dx) (y + dy) doc with
            | ValueSome n when
              not n.IsPath && n.Terrain <> tile.Terrain && frame.IsNone
              ->
              let name =
                $"{MapGround.kindName n.Terrain}_patch_on_{MapGround.kindName tile.Terrain}_{side}"

              frame <- Tiles.tryByName name
            | _ -> ()

          match frame with
          | ValueSome f ->
            CellGrid2D.set x y { tile with Decoration = ValueSome f } doc
          | ValueNone -> ())
      doc

  /// Tile-true meaning for the landmark scan: the road and the
  /// obstacle props are the no-build cells. The tag grid is the
  /// walking-query surface the Buildable layer derives from.
  let private tileTags (_: int) (_: int) (tile: MapTile) : string seq =
    if tile.IsPath || not tile.Buildable then
      [ "no-build" ]
    else
      []

  /// Mark the road's vertex cells on the document (spawn/base and
  /// turn markers — the Waypoints layer reads IsWaypoint).
  let private markWaypoints
    (cells: struct (int * int)[])
    (doc: CellGrid2D<MapTile>)
    =
    for struct (x, y) in cells do
      match CellGrid2D.get x y doc with
      | ValueSome t -> CellGrid2D.set x y { t with IsWaypoint = true } doc
      | ValueNone -> ()

  /// Split the authored document grid into the MapModel's parallel
  /// layers — the document is the single source of truth; each layer
  /// projects its slice of it.
  let private splitLayers
    (doc: CellGrid2D<MapTile>)
    (marks: Landmarks)
    : LayeredMap<MapTile> =
    let grid = LayeredMap.create doc.Width doc.Height doc.CellSize doc.Origin

    let struct (terrainL, _) = LayeredMap.getOrAddLayer MapLayers.Terrain grid

    let struct (buildableL, _) =
      LayeredMap.getOrAddLayer MapLayers.Buildable grid

    let struct (pathL, _) = LayeredMap.getOrAddLayer MapLayers.Path grid

    let struct (waypointL, _) =
      LayeredMap.getOrAddLayer MapLayers.Waypoints grid

    let struct (decoL, _) = LayeredMap.getOrAddLayer MapLayers.Decorations grid

    // Buildability reads the tag grid's walking query — the
    // landmarks are the query surface, the layer the cache.
    CellGrid2D.iter
      (fun x y tile ->
        CellGrid2D.set x y tile terrainL

        CellGrid2D.set
          x
          y
          {
            tile with
                Buildable = not(Flow.isTag "no-build" { X = x; Y = y } marks)
          }
          buildableL

        if tile.IsPath then
          CellGrid2D.set x y tile pathL

        if tile.Decoration.IsSome then
          CellGrid2D.set x y tile decoL

        if tile.IsWaypoint then
          CellGrid2D.set x y tile waypointL)
      doc

    grid

  /// The Old Harbour level document — zone structure by layout,
  /// clutter by stamps, road anchors read back from the zones.
  ///
  /// Layout: a sand shore across the top (the dockside), then four
  /// zones west→east — woods (grass), plaza (stone, the depot yard),
  /// flats (dirt, rock garden), rise (grass, the base hill). The
  /// road runs spawn → woods → shore bluff → flats → base: an S
  /// through every zone, waypoints resolved FROM the zone rects.
  let private harbour
    (cfg: WorldConfig)
    : struct (CellGrid2D<MapTile> * struct (int * int)[] * Landmarks) =
    let cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    let doc = CellGrid2D.create cfg.GridCols cfg.GridRows cellSize Vector2.Zero

    // Clutter vocabulary beyond the curated semantic names.
    let sprig = Tiles.byName["plant_sprig"]
    let treeLarge = Tiles.byName["tree_large"]

    let shore =
      zone "shore" TerrainKind.Sand cfg.Seed [
        // dockside: containers and crates by the water
        Tiles.containerLarge, true, 2
        Tiles.containerSmall, true, 1
        Tiles.crateMetalBeveled, true, 2
        Tiles.crateMetalDiamond, true, 1
        Tiles.rockMedium, true, 2
        Tiles.rockSmall, true, 2
        sprig, false, 6
        Tiles.bushSmall, false, 2
      ]

    let woods =
      zone "woods" TerrainKind.Grass (cfg.Seed + 100) [
        Tiles.treeRound, true, 6
        Tiles.treePine, true, 5
        treeLarge, true, 3
        Tiles.bushSmall, false, 4
        sprig, false, 3
      ]

    let plaza =
      zone "plaza" TerrainKind.Stone (cfg.Seed + 200) [
        // the depot yard: crate stacks
        Tiles.crateMetalSquare, true, 3
        Tiles.crateMetalOctagon, true, 3
        Tiles.containerLarge, true, 1
        Tiles.containerSmall, true, 1
        Tiles.rockSmall, true, 2
        sprig, false, 3
      ]

    let flats =
      zone "flats" TerrainKind.Dirt (cfg.Seed + 300) [
        Tiles.rockLarge, true, 2
        Tiles.rockMedium, true, 3
        Tiles.rockSmall, true, 3
        Tiles.bushSmall, false, 2
      ]

    let rise =
      zone "rise" TerrainKind.Grass (cfg.Seed + 400) [
        Tiles.treePine, true, 2
        Tiles.treeRound, true, 2
        Tiles.rockSmall, true, 1
        sprig, false, 2
      ]

    // A dockside depot, docked top-right on the shore: three props
    // in a row with a one-cell gap. Named — an anchor other systems
    // can read back (loot drop point, minimap marker, ...).
    let depot =
      Flow.docked {
        Anchor = Dock.Top ||| Dock.Right
        // one cell in from each anchored edge
        Inset = {
          InsetSpec.Zero with
              Top = 1
              Right = 1
        }
        Stamp =
          Flow.row { FlowOpts.Default with Gap = 1 } [
            Flow.prop(obstacle TerrainKind.Sand Tiles.crateMetalSquare)
            Flow.prop(obstacle TerrainKind.Sand Tiles.crateMetalOctagon)
            Flow.prop(obstacle TerrainKind.Sand Tiles.containerSmall)
          ]
          |> Stamp.named "depot"
      }

    let level =
      Flow.overlay [
        // the grid carries fixed rows, so it has a footprint: `stretch`
        // mounts it as the full-bleed base layer the overlay expects
        Flow.stretch(
          Flow.grid {
            Cols = [| Weight 6f; Weight 7f; Weight 4f; Weight 3f |]
            Rows = [| Fixed 3; Weight 1f |]
            Gap = 0
            Areas = [| "shore shore shore shore"; "woods plaza flats rise" |]

            Places = [|
              struct (Place.Area "shore", shore)
              struct (Place.Area "woods", woods)
              struct (Place.Area "plaza", plaza)
              struct (Place.Area "flats", flats)
              struct (Place.Area "rise", rise)
            |]
          }
        )

        depot
      ]

    let struct (_, marks) = doc |> Flow.run level

    // The road: spawn at the woods' west gate, east along the band's
    // mid row, north to the shore road, east along the bluff, south
    // through the flats, base at the rise's east gate.
    let cells =
      match
        Flow.tryPosition "shore" marks,
        Flow.tryPosition "woods" marks,
        Flow.tryPosition "plaza" marks,
        Flow.tryPosition "flats" marks,
        Flow.tryPosition "rise" marks
      with
      | ValueSome shoreR,
        ValueSome woodsR,
        ValueSome plazaR,
        ValueSome flatsR,
        ValueSome riseR ->
        let shoreRoad = shoreR.Y + shoreR.H // first row under the shore
        let mid = woodsR.Y + woodsR.H / 2 // the band's mid row
        let plazaMid = plazaR.X + plazaR.W / 2
        let flatsMid = flatsR.X + flatsR.W / 2

        [|
          struct (woodsR.X, mid) // spawn — the woods' west gate
          struct (plazaMid, mid) // east through the woods
          struct (plazaMid, shoreRoad) // north to the shore road
          struct (flatsMid, shoreRoad) // east along the bluff
          struct (flatsMid, flatsR.Y + flatsR.H / 2) // south through the flats
          struct (riseR.X + riseR.W - 1, flatsR.Y + flatsR.H / 2) // base — east gate
        |]
      | _ ->
        failwith
          "Old Harbour: zone landmarks missing (a named zone was not placed)"

    for i in 1 .. cells.Length - 1 do
      carveSegment doc cells[i - 1] cells[i]

    markWaypoints cells doc
    blendZones doc

    // Derive the per-cell tag grid from the finished tiles — the
    // Buildable layer reads the "no-build" walking query below.
    let marks = Landmarks.scanTiles tileTags doc marks

    struct (doc, cells, marks)

  /// One axis-aligned road segment as a stamp — a positioned section
  /// carrying a Flow repeat (repeatX for horizontal, repeatY for
  /// vertical — inclusive of both endpoints).
  let inline private stampSegment
    (struct (px, py): struct (int * int))
    (struct (tx, ty): struct (int * int))
    (section: GridSection2D<MapTile>)
    : GridSection2D<MapTile> =
    if py = ty then
      section
      |> Layout.section (min px tx) py (fun inner ->
        Flow.repeatX (abs(tx - px) + 1) pathTile inner
        inner)
    else
      section
      |> Layout.section px (min py ty) (fun inner ->
        Flow.repeatY (abs(ty - py) + 1) pathTile inner
        inner)

  // ── Level-2 procedural generation ──

  /// Prop frame picked deterministically from the placement cell — no
  /// extra RNG stream (Kimo's rule: RNG streams are owned, never shared).
  let inline private propFor (x: int) (y: int) : TileInfo =
    Tiles.decoProps[(x * 7 + y * 13) % Tiles.decoProps.Length]

  /// Deterministic roll in [0, 1) from a cell + salt (same rule).
  let inline private hashRoll (x: int) (y: int) (salt: int) : float =
    float((x * 31 + y * 17 + salt * 7) % 997) / 997.0

  /// Scatter props as OBSTACLES: the Decorations layer gets the prop
  /// row, the Buildable layer is cleared under it. The stamp's section
  /// offset IS the placement cell — prop variety derives from it.
  let private scatterObstacles
    (count: int)
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (buildable: CellGrid2D<MapTile>)
    : unit =
    Layout.scatterStamp
      count
      seed
      (fun s ->
        let gx = s.OffsetX
        let gy = s.OffsetY
        let frame = propFor gx gy
        CellGrid2D.set gx gy (obstacleTile frame) deco
        CellGrid2D.set gx gy nonBuildableTile buildable
        s)
      (createSection deco)
    |> ignore

  /// Visual-only props (HandAuthored): decoration rows, never on the
  /// road, buildability untouched.
  let private scatterVisualProps
    (count: int)
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (pathLayer: CellGrid2D<MapTile>)
    : unit =
    Layout.scatterStamp
      count
      seed
      (fun s ->
        let gx = s.OffsetX
        let gy = s.OffsetY

        let onPath =
          pathLayer |> CellGrid2D.get gx gy |> ValueOption.exists _.IsPath

        if onPath then
          s
        else
          CellGrid2D.set gx gy (decoTile(propFor gx gy)) deco
          s)
      (createSection deco)
    |> ignore

  /// Dirt blends hugging the road — ONE coherent family (dirt on
  /// grass: dots, patch edges, circle corners), each frame oriented by
  /// the neighbor's direction from the road cell. Props keep their
  /// spot (blends only fill empty decoration cells).
  let private scatterBlends
    (seed: int)
    (deco: CellGrid2D<MapTile>)
    (pathLayer: CellGrid2D<MapTile>)
    : unit =
    CellGrid2D.iter
      (fun x y tile ->
        if tile.IsPath then
          for struct (nx, ny) in Grid2DSpatial.neighbors4 x y pathLayer do
            // The Path layer is sparse: an absent cell is grass (free);
            // only a PRESENT path-marked cell is the road.
            let freeGrass =
              match pathLayer |> CellGrid2D.get nx ny with
              | ValueSome t -> not t.IsPath
              | ValueNone -> true

            let empty = (deco |> CellGrid2D.get nx ny).IsNone

            if freeGrass && empty && hashRoll nx ny seed < 0.45 then
              let r = hashRoll nx ny (seed + 1)
              let dx = nx - x
              let dy = ny - y

              let frame =
                if r < 0.2 then
                  Tiles.dirtDotOnGrass
                elif dx <> 0 && dy <> 0 then
                  if dx < 0 && dy < 0 then Tiles.dirtCircleOnGrassTL
                  elif dx > 0 && dy < 0 then Tiles.dirtCircleOnGrassTR
                  elif dx < 0 then Tiles.dirtCircleOnGrassBL
                  else Tiles.dirtCircleOnGrassBR
                elif dx < 0 then
                  Tiles.dirtPatchOnGrassLeft
                elif dx > 0 then
                  Tiles.dirtPatchOnGrassRight
                elif dy < 0 then
                  Tiles.dirtPatchOnGrassTop
                else
                  Tiles.dirtPatchOnGrassBottom

              CellGrid2D.set nx ny (decoTile frame) deco)
      pathLayer

  /// One procedural attempt on a FRESH grid: obstacles scattered with
  /// the given seed, the road carved by findPath around them, and a
  /// floodFill reachability validation (independent of A*).
  let private tryProcedural
    (cfg: WorldConfig)
    (seed: int)
    : struct (LayeredMap<MapTile> *
      struct (int * int)[] *
      struct (int * int) *
      struct (int * int)) voption
    =
    let cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    let grid =
      LayeredMap.create cfg.GridCols cfg.GridRows cellSize Vector2.Zero
      |> LayeredMap.runLayer MapLayers.Terrain (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)
      |> LayeredMap.runLayer MapLayers.Buildable (fun s ->
        Layout.fill 0 0 cfg.GridCols cfg.GridRows grassTile s)

    let struct (deco, _) = LayeredMap.getOrAddLayer MapLayers.Decorations grid

    let struct (buildable, _) =
      LayeredMap.getOrAddLayer MapLayers.Buildable grid

    let struct (terrain, _) = LayeredMap.getOrAddLayer MapLayers.Terrain grid

    let obstacleCount = cfg.GridCols * cfg.GridRows / 10
    scatterObstacles obstacleCount seed deco buildable

    let rng = System.Random(seed)
    let spawnY = rng.Next(1, cfg.GridRows - 1)
    let baseY = rng.Next(1, cfg.GridRows - 1)

    let isPassable x y =
      match deco |> CellGrid2D.get x y with
      | ValueSome t -> t.Buildable
      | ValueNone -> true

    match
      Grid2DSpatial.findPath
        0
        spawnY
        (cfg.GridCols - 1)
        baseY
        isPassable
        (fun _ _ _ _ -> 1f)
        terrain
    with
    | ValueNone -> ValueNone
    | ValueSome pathCells ->
      // floodFill validation: the base must be reachable from spawn
      // over non-obstacle cells (independent of the A* result).
      let reachable = Grid2DSpatial.floodFill 0 spawnY isPassable terrain

      let baseReachable =
        reachable
        |> Array.exists(fun struct (x, y) ->
          struct (x, y) = struct (cfg.GridCols - 1, baseY))

      if not baseReachable then
        ValueNone
      else
        // Carve the road along the found path (stamp machinery — the
        // path is 4-adjacent, so each pair is one repeatX/repeatY).
        let struct (pathLayer, _) = LayeredMap.getOrAddLayer MapLayers.Path grid

        for i in 1 .. pathCells.Length - 1 do
          stampSegment pathCells[i - 1] pathCells[i] (createSection pathLayer)
          |> ignore

          stampSegment pathCells[i - 1] pathCells[i] (createSection buildable)
          |> ignore

        // Waypoints: every path cell is a waypoint (spawn/base markers
        // ride on the same layer — the view keys the base mount on
        // BaseCell).
        let waypointTile = { grassTile with IsWaypoint = true }

        grid
        |> LayeredMap.runLayer MapLayers.Waypoints (fun s ->
          pathCells
          |> Array.fold
            (fun acc struct (x, y) -> Layout.set x y waypointTile acc)
            s)
        |> ignore

        ValueSome(
          grid,
          pathCells,
          struct (0, spawnY),
          struct (cfg.GridCols - 1, baseY)
        )

  /// Shared tail: world-space path centers.
  let private buildModel
    (grid: LayeredMap<MapTile>)
    (pathCells: struct (int * int)[])
    (spawn: struct (int * int))
    (baseCell: struct (int * int))
    : MapModel =
    let cellSize = Vector2(float32 Tiles.TileSize, float32 Tiles.TileSize)

    let struct (terrainLayer, _) =
      LayeredMap.getOrAddLayer MapLayers.Terrain grid

    let path =
      pathCells
      |> Array.map(fun struct (x, y) ->
        let topLeft = CellGrid2D.getWorldPos x y terrainLayer
        topLeft + cellSize / 2f)

    {
      Grid = grid
      Path = path
      SpawnCell = spawn
      BaseCell = baseCell
    }

  /// Level-1: the Old Harbour document — zones, props, and a road
  /// whose waypoints the layout resolved.
  let private handAuthored(cfg: WorldConfig) : MapModel =
    let struct (doc, cells, marks) = harbour cfg
    let grid = splitLayers doc marks

    buildModel grid cells cells[0] cells[cells.Length - 1]

  /// Level-2: seeded obstacle scatter → findPath road → floodFill
  /// validation. Seeds advance until a valid layout lands; after 16
  /// attempts it falls back to the hand-authored map (guaranteed
  /// valid — the game never boots to a broken map).
  let private procedural(cfg: WorldConfig) : MapModel =
    let rec attempt (seed: int) (left: int) : MapModel =
      if left = 0 then
        handAuthored cfg
      else
        match tryProcedural cfg seed with
        | ValueSome struct (grid, pathCells, spawn, baseCell) ->
          // The procedural map is single-terrain (grass): the road
          // edges get the dirt-on-grass blend pass.
          let struct (deco, _) =
            LayeredMap.getOrAddLayer MapLayers.Decorations grid

          let struct (pathLayer, _) =
            LayeredMap.getOrAddLayer MapLayers.Path grid

          scatterBlends cfg.Seed deco pathLayer

          buildModel grid pathCells spawn baseCell
        | ValueNone -> attempt (seed + 1) (left - 1)

    attempt cfg.Seed 16

  let create(cfg: WorldConfig) : MapModel =
    match cfg.MapVariant with
    | MapVariant.HandAuthored -> handAuthored cfg
    | MapVariant.Procedural -> procedural cfg
