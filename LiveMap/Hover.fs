module LiveMap.Hover

open System
open System.Numerics
open LiveMap
open LiveMap.Semantics
open Mibo.Layout
open Mibo.Markup
open Raylib_cs

/// What the pointer is over: the cell, the layer that painted it, the word
/// it painted, whether that word blocks movement, and the smallest
/// document region that covers the cell.
[<Struct>]
type Info = {
  Cell: struct (int * int)
  /// The name of the layer whose paint answered for this cell. The
  /// topmost layer that painted the cell wins, so the panel names what the
  /// reader sees.
  Layer: string
  Word: string
  Solid: bool
  /// The name of the innermost element that painted this cell. A cell a
  /// kernel painted alone sits in no region.
  Region: string voption
  Rect: CellRect voption
}

/// The smallest region covering a cell, in one layer's landmarks.
///
/// Regions are the resolved rectangles of the document's elements, so one
/// cell can sit inside several: a yard inside a district inside the map.
/// The smallest rectangle is the innermost element that painted the cell,
/// which is the one a reader wants named. Equal areas go to the region
/// recorded first, which is the one painted last.
let regionAt
  (x: int)
  (y: int)
  (landmarks: Landmarks)
  : struct (string * CellRect) voption =
  let mutable found = ValueNone
  let mutable smallest = Int32.MaxValue

  for entry in landmarks.Tagged do
    for rect in entry.Value do
      let covers =
        x >= rect.X && x < rect.X + rect.W && y >= rect.Y && y < rect.Y + rect.H

      if covers && rect.W * rect.H < smallest then
        smallest <- rect.W * rect.H
        found <- ValueSome(struct (entry.Key, rect))

  found

let private describe
  (x: int)
  (y: int)
  (layer: string)
  (word: string)
  (solid: bool)
  (landmarks: Landmarks)
  : Info =
  // the smallest region covering the cell, split into the two halves the
  // panel reads: a cell a kernel painted alone sits in none
  let region = regionAt x y landmarks

  {
    Cell = struct (x, y)
    Layer = layer
    Word = word
    Solid = solid
    Region = region |> ValueOption.map(fun struct (name, _) -> name)
    Rect = region |> ValueOption.map(fun struct (_, rect) -> rect)
  }

/// The topmost layer that painted a cell, and what it painted there.
///
/// Layers stack bottom first, so the walk starts at the top: the first
/// layer with a cell at that position is the one the reader sees. The
/// hover and `--check` both read this walk.
let tryCellAt
  (layers: DocFlow.BuiltLayer<'T>[])
  (x: int)
  (y: int)
  : struct (DocFlow.BuiltLayer<'T> * 'T) voption =
  let mutable found = ValueNone
  let mutable i = layers.Length - 1

  while found.IsNone && i >= 0 do
    found <-
      CellGrid2D.get x y layers[i].Grid
      |> ValueOption.map(fun cell -> struct (layers[i], cell))

    i <- i - 1

  found

/// The region of the layer that answers for a cell, described for the
/// panel. The region comes from that layer's own landmarks, so an element
/// name that repeats across layers reports the rectangle of the layer that
/// answered.
let private probe
  (layers: DocFlow.BuiltLayer<'T>[])
  (x: int)
  (y: int)
  (word: 'T -> string)
  (solid: 'T -> bool)
  : Info voption =
  tryCellAt layers x y
  |> ValueOption.map(fun struct (layer, cell) ->
    describe x y layer.Name (word cell) (solid cell) layer.Landmarks)

/// The cell under a screen point on a flat map, and what sits there.
let flat
  (camera: Camera2D)
  (screen: Vector2)
  (layers: DocFlow.BuiltLayer<Cell>[])
  : Info voption =
  if layers.Length = 0 then
    ValueNone
  else
    // every layer spans the same map, so the bottom grid answers the pick
    // for all of them
    Raylib.GetScreenToWorld2D(screen, camera)
    |> (fun world -> Grid2DSpatial.worldToCell world layers[0].Grid)
    |> ValueOption.bind(fun struct (x, y) ->
      probe layers x y (fun cell -> Palette.wordOfTile cell.Tile) (fun cell ->
        cell.Solid))

/// The cell under a screen point on a block map. The map is a footprint
/// on the ground plane, so the pick is a ray against y = 0; a ray that
/// points at the sky meets nothing.
let blocks
  (camera: Camera3D)
  (screen: Vector2)
  (layers: DocFlow.BuiltLayer<BlockCell>[])
  : Info voption =
  if layers.Length = 0 then
    ValueNone
  else
    let ray = Raylib.GetScreenToWorldRay(screen, camera)

    if abs ray.Direction.Y < 1e-6f then
      ValueNone
    else
      let distance = -ray.Position.Y / ray.Direction.Y

      if distance <= 0f then
        ValueNone
      else
        let hit = ray.Position + ray.Direction * distance

        Grid2DSpatial.worldToCell (Vector2(hit.X, hit.Z)) layers[0].Grid
        |> ValueOption.bind(fun struct (x, y) ->
          probe layers x y (fun cell -> cell.Model.Name) (fun cell ->
            cell.Solid))
