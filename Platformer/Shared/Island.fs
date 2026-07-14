/// Floating island container — dynamic walls + projected content.
/// Islands are sealed shells with proper corner tiles at every edge and gap.
/// Each wall is a stamp composed by the consumer, so gaps, mixed fills,
/// and internal structures (floors, ledges, caves) are all declarative.
module Platformer.Island

open Mibo.Layout
open Platformer.Types
open Platformer.Stamps

/// Wall fill style — determines which tile family a wall uses.
[<Struct>]
type WallFill =
  | Block
  | Cloud
  | Ledge
  | Overhang

// -----------------------------------------------------------
// Internal helpers
// -----------------------------------------------------------

/// Vertical row with distinct top/middle/bottom tiles.
///   height ≤ 0 → no-op
///   height  1   → singleTile only
///   height  2   → top + bottom
///   height ≥ 3 → top + middle × (height-2) + bottom
let private vRow
  (height: int)
  (topTile: Tile)
  (middleTile: Tile)
  (bottomTile: Tile)
  (singleTile: Tile)
  (section: GridSection2D<Tile>)
  : GridSection2D<Tile> =
  match height with
  | n when n <= 0 -> section
  | 1 -> section |> Layout.set 0 0 singleTile
  | 2 -> section |> Layout.set 0 0 topTile |> Layout.set 0 1 bottomTile
  | _ ->
    section
    |> Layout.set 0 0 topTile
    |> Layout.repeatY 0 1 (height - 2) middleTile
    |> Layout.set 0 (height - 1) bottomTile

// -----------------------------------------------------------
// Wall stamps
// -----------------------------------------------------------

/// Horizontal top wall — draws at local y=0, spans the given length.
/// Block → BlockTopLeft / BlockTop / BlockTopRight (standalone Block)
/// Cloud → CloudLeft / CloudMiddle / CloudRight (standalone Cloud)
/// Ledge → HorizontalLeft / Horizontal / HorizontalRight
/// Overhang → HorizontalOverhangLeft / Horizontal / HorizontalOverhangRight
let topWall (biome: Biome) (length: int) (fill: WallFill) =
  match fill with
  | Block ->
    Stamps.hRow
      length
      (BlockTopLeft biome)
      (BlockTop biome)
      (BlockTopRight biome)
      (Tile.Block biome)
  | Cloud ->
    Stamps.hRow
      length
      (CloudLeft biome)
      (CloudMiddle biome)
      (CloudRight biome)
      (Tile.Cloud biome)
  | Ledge ->
    Stamps.hRow
      length
      (HorizontalLeft biome)
      (Horizontal biome)
      (HorizontalRight biome)
      (Horizontal biome)
  | Overhang ->
    Stamps.hRow
      length
      (HorizontalOverhangLeft biome)
      (Horizontal biome)
      (HorizontalOverhangRight biome)
      (Horizontal biome)

/// Horizontal bottom wall — draws at local y=0, spans the given length.
/// Block → BlockBottomLeft / BlockBottom / BlockBottomRight (standalone Block)
/// Cloud → CloudLeft / CloudMiddle / CloudRight (standalone Cloud)
/// Ledge → HorizontalLeft / Horizontal / HorizontalRight
/// Overhang → HorizontalOverhangLeft / Horizontal / HorizontalOverhangRight
let bottomWall (biome: Biome) (length: int) (fill: WallFill) =
  match fill with
  | Block ->
    Stamps.hRow
      length
      (BlockBottomLeft biome)
      (BlockBottom biome)
      (BlockBottomRight biome)
      (Tile.Block biome)
  | Cloud ->
    Stamps.hRow
      length
      (CloudLeft biome)
      (CloudMiddle biome)
      (CloudRight biome)
      (Tile.Cloud biome)
  | Ledge ->
    Stamps.hRow
      length
      (HorizontalLeft biome)
      (Horizontal biome)
      (HorizontalRight biome)
      (Horizontal biome)
  | Overhang ->
    Stamps.hRow
      length
      (HorizontalOverhangLeft biome)
      (Horizontal biome)
      (HorizontalOverhangRight biome)
      (Horizontal biome)

/// Vertical left wall — draws at local x=0, spans the given height.
/// Uses block tiles: BlockTopLeft / BlockLeft / BlockBottomLeft.
let leftWall (biome: Biome) (height: int) =
  vRow
    height
    (BlockTopLeft biome)
    (BlockLeft biome)
    (BlockBottomLeft biome)
    (Tile.Block biome)

/// Vertical right wall — draws at local x=0, spans the given height.
/// Uses block tiles: BlockTopRight / BlockRight / BlockBottomRight.
let rightWall (biome: Biome) (height: int) =
  vRow
    height
    (BlockTopRight biome)
    (BlockRight biome)
    (BlockBottomRight biome)
    (Tile.Block biome)

// -----------------------------------------------------------
// Island definition and creation
// -----------------------------------------------------------

[<Struct>]
type IslandDefinition = {
  Width: int
  Height: int
  Top: GridSection2D<Tile> -> GridSection2D<Tile>
  Bottom: GridSection2D<Tile> -> GridSection2D<Tile>
  Left: GridSection2D<Tile> -> GridSection2D<Tile>
  Right: GridSection2D<Tile> -> GridSection2D<Tile>
  Content: (GridSection2D<Tile> -> GridSection2D<Tile>) voption
}

/// Create a floating island from a definition.
/// Walls are applied at island edges; content is projected onto the
/// full island sub-section (relative 0,0) and can override wall tiles.
/// Execution order: Left → Right → Top → Bottom → Content.
/// Horizontal walls (Top/Bottom) are applied last so they own the corners —
/// a cloud bottom wall keeps CloudLeft/CloudRight at the corners instead of
/// being overwritten by the vertical wall's block corner tiles.
let create (def: IslandDefinition) (section: GridSection2D<Tile>) =
  section
  |> Layout.section 0 0 def.Left
  |> Layout.section (def.Width - 1) 0 def.Right
  |> Layout.section 0 0 def.Top
  |> Layout.section 0 (def.Height - 1) def.Bottom
  |> ignore

  match def.Content with
  | ValueSome content -> section |> Layout.section 0 0 content
  | ValueNone -> section
