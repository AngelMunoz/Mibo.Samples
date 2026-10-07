namespace SpaceBattle

open System
open Mibo.Animation
open Mibo
open Mibo.Elmish
open Mibo.Elmish.Graphics
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Layout
open Mibo.Elmish.Graphics2D
open Raylib_cs
open SpaceBattle.Types
open SpaceBattle.Units

type MapModel = {
  Grid: CellGrid2D<Tile>
  Seed: int
  Reachable: Set<struct (int * int)>
  Visible: Set<struct (int * int)>
  AttackTargets: Set<struct (int * int)>
  Path: struct (int * int)[]
}

[<Struct>]
type RangeQuery = {
  Selection: SelectionState
  Hovered: struct (int * int) voption
  Units: Map<struct (int * int), SBUnit>
  CurrentPlayerIndex: int
  CanMove: bool
  CanAct: bool
}

[<Struct>]
type MapMsg =
  | RecalculateRange of query: RangeQuery
  | RefreshVisibility of
    units: Map<struct (int * int), SBUnit> *
    playerIndex: int

module Map =
  open System.Numerics

  let inline createMap origin width height : CellGrid2D<Tile> =
    CellGrid2D.createHex {
      Orientation = HexOrientation.FlatTop
      Width = width
      Height = height
      Radius = Constants.CellSize
      Origin = origin
    }

  /// The asteroid belt: crates along the rim, asteroids scattered inside.
  /// One Flow canvas painted in order — the deep-space floor first, the
  /// scatter styles over it. The seed draws consume the rng in the same
  /// order the retired HexLayout pipeline did, so a given seed builds the
  /// same board.
  let fillMap (rng: Random) (map: CellGrid2D<Tile>) : CellGrid2D<Tile> =
    let belt =
      Flow.canvas [
        Flow.fill DeepSpace
        Flow.scatterBorder { Count = 5; Seed = rng.Next() } Crate1

        Flow.noise
          {
            Count = rng.Next(10)
            Seed = rng.Next()
          }
          Asteroid1
        Flow.noise
          {
            Count = rng.Next(5)
            Seed = rng.Next()
          }
          Asteroid2
      ]

    let struct (grid, _) = map |> Flow.run belt

    grid

  let init(seed: int, width: int, height: int) : MapModel =
    let grid = createMap Vector2.Zero width height |> fillMap(Random seed)
    let w, h = grid.Width, grid.Height

    let corners = [|
      struct (0, 0)
      struct (1, 0)
      struct (0, 1)
      struct (w - 1, h - 1)
      struct (w - 1, h - 2)
      struct (w - 2, h - 1)
      struct (w - 1, 0)
      struct (w - 2, 0)
      struct (w - 1, 1)
      struct (0, h - 1)
      struct (1, h - 1)
      struct (0, h - 2)
    |]

    for struct (c, r) in corners do
      CellGrid2D.set c r DeepSpace grid

    {
      Grid = grid
      Seed = seed
      Reachable = Set.empty
      AttackTargets = Set.empty
      Visible = Set.empty
      Path = [||]
    }

  let private pathIndexMap
    (path: struct (int * int)[])
    : Map<struct (int * int), int> =
    path |> Array.mapi(fun i cell -> cell, i) |> Map.ofArray

  let private pathGradientColor (pathLen: int) (idx: int) =
    let t = float32 idx / float32(pathLen - 1)
    let alpha = 80uy + byte(t * 160f)
    Color.create 100uy 200uy 255uy alpha

  // Exact raylib palette bytes — the retired pipe module took raylib colors,
  // and Mibo.Color's presets (Green, Blue, ...) carry different bytes.
  let private rlRed = Color.rgb 230uy 41uy 55uy

  let private rlViolet = Color.rgb 135uy 60uy 190uy

  let private rlBlue = Color.rgb 0uy 121uy 241uy

  let private rlDarkBlue = Color.rgb 0uy 82uy 172uy

  let private rlGreen = Color.rgb 0uy 228uy 48uy

  let private rlDarkGray = Color.rgb 80uy 80uy 80uy

  let private rlYellow = Color.rgb 253uy 249uy 0uy

  let computeVisibleUnits
    (units: Map<struct (int * int), SBUnit>)
    (playerIndex: int)
    (grid: CellGrid2D<Tile>)
    : Set<struct (int * int)> =
    let mutable visible = Set.empty

    for KeyValue(struct (col, row), unit) in units do
      if unit.PlayerIndex = playerIndex then
        let cells = Hex2DSpatial.inRange col row unit.VisualRange grid

        for cell in cells do
          visible <- visible |> Set.add cell

    visible

  let update (msg: MapMsg) (model: MapModel) : MapModel =
    match msg with
    | RecalculateRange query ->
      match query.Selection with
      | NoSelection -> {
          model with
              Reachable = Set.empty
              AttackTargets = Set.empty
              Path = [||]
        }
      | Selected cell ->
        let struct (col, row) = cell

        match query.Units |> Map.tryFind cell with
        | None -> {
            model with
                Reachable = Set.empty
                AttackTargets = Set.empty
                Path = [||]
          }
        | Some unit ->
          if query.CanMove && query.CanAct then
            let reachable =
              Selection.computeMoveRange
                col
                row
                unit.MoveRange
                model.Grid
                query.Units
                query.CurrentPlayerIndex

            let attackTargets =
              Selection.computeAttackRange col row unit.AttackRange model.Grid

            let path =
              match query.Hovered with
              | ValueSome dest when reachable.Contains dest ->
                Selection.computePath
                  cell
                  dest
                  model.Grid
                  query.Units
                  query.CurrentPlayerIndex
              | _ -> [||]

            {
              model with
                  Reachable = reachable
                  AttackTargets = attackTargets
                  Path = path
            }
          elif query.CanMove then
            let reachable =
              Selection.computeMoveRange
                col
                row
                unit.MoveRange
                model.Grid
                query.Units
                query.CurrentPlayerIndex

            let path =
              match query.Hovered with
              | ValueSome dest when reachable.Contains dest ->
                Selection.computePath
                  cell
                  dest
                  model.Grid
                  query.Units
                  query.CurrentPlayerIndex
              | _ -> [||]

            {
              model with
                  Reachable = reachable
                  AttackTargets = Set.empty
                  Path = path
            }
          elif query.CanAct then
            let attackTargets =
              Selection.computeAttackRange col row unit.AttackRange model.Grid

            {
              model with
                  Reachable = Set.empty
                  AttackTargets = attackTargets
                  Path = [||]
            }
          else
            {
              model with
                  Reachable = Set.empty
                  AttackTargets = Set.empty
                  Path = [||]
            }

    | RefreshVisibility(units, playerIndex) ->
        {
          model with
              Visible = computeVisibleUnits units playerIndex model.Grid
        }

  let viewTiles
    (vpWidth: float32)
    (vpHeight: float32)
    (sprites: Map<struct (int * int), AnimatedSprite>)
    (camera: Camera2D)
    (mapModel: MapModel)
    (lightCtx: LightContext2D)
    (buffer: RenderBuffer2D)
    =
    let model = mapModel.Grid
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

    model
    |> CellGrid2D.iterVisible
      (int topLeft.X)
      (int topLeft.Y)
      (int bottomRight.X)
      (int bottomRight.Y)
      (fun col row tile ->
        let worldPos = model |> CellGrid2D.getWorldPos col row
        let hexW = Constants.CellSize * 2.0f
        let hexH = Constants.CellSize * sqrt 3.0f

        let targetRect =
          Rectangle(worldPos.X - hexW / 2f, worldPos.Y - hexH / 2f, hexW, hexH)

        let color =
          match tile with
          | Asteroid1 -> rlRed
          | Asteroid2 -> rlViolet
          | Crate1 -> rlBlue
          | Crate2 -> rlDarkBlue
          | Station -> rlGreen
          | DeepSpace -> rlDarkGray

        match sprites |> Map.tryFind struct (col, row) with
        | Some animated ->
          let source = AnimatedSprite.currentSource animated
          let texture = animated.Sheet.Texture

          buffer
            .litSprite(
              lightCtx,
              SpriteState.create(texture, targetRect, source)
            )
            .drop()
        | None ->
          buffer
            .polyOutline(
              Vector2(worldPos.X, worldPos.Y),
              6,
              Constants.CellSize,
              0f,
              color,
              thickness = 1f
            )
            .drop())

    buffer

  let viewOverlays
    (vpWidth: float32)
    (vpHeight: float32)
    (camera: Camera2D)
    (mapModel: MapModel)
    (hoveredOver: struct (int * int) voption)
    (buffer: RenderBuffer2D)
    =
    let model = mapModel.Grid
    let reachable = mapModel.Reachable
    let attackTargets = mapModel.AttackTargets
    let path = mapModel.Path
    let topLeft = Raylib.GetScreenToWorld2D(Vector2.Zero, camera)

    let bottomRight =
      Raylib.GetScreenToWorld2D(Vector2(vpWidth, vpHeight), camera)

    let pathIdx = pathIndexMap path

    model
    |> CellGrid2D.iterVisible
      (int topLeft.X)
      (int topLeft.Y)
      (int bottomRight.X)
      (int bottomRight.Y)
      (fun col row tile ->
        let worldPos = model |> CellGrid2D.getWorldPos col row

        if attackTargets.Contains(struct (col, row)) then
          buffer
            .fillPoly(
              Vector2(worldPos.X, worldPos.Y),
              6,
              Constants.CellSize,
              0f,
              Color.create 255uy 80uy 80uy 120uy
            )
            .drop()

        if reachable.Contains(struct (col, row)) then
          buffer
            .fillPoly(
              Vector2(worldPos.X, worldPos.Y),
              6,
              Constants.CellSize,
              0f,
              Color.create 100uy 180uy 255uy 100uy
            )
            .drop()

        match pathIdx |> Map.tryFind struct (col, row) with
        | Some idx when path.Length > 1 ->
          buffer
            .fillPoly(
              Vector2(worldPos.X, worldPos.Y),
              6,
              Constants.CellSize,
              0f,
              pathGradientColor path.Length idx
            )
            .drop()
        | Some _
        | None -> ()

        match hoveredOver with
        | ValueSome struct (hCol, hRow) ->
          let hWorldPos = model |> CellGrid2D.getWorldPos hCol hRow

          buffer
            .polyOutline(
              Vector2(hWorldPos.X, hWorldPos.Y),
              6,
              Constants.CellSize,
              0f,
              rlYellow,
              thickness = 2.5f
            )
            .drop()
        | ValueNone -> ())

    buffer
