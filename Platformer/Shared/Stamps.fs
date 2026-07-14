/// Start/middle/end aware platformer stamps built on the Mibo Layout DSL.
/// Each stamp picks the correct tile variant (corner, edge, center) based on length,
/// so generated terrain has proper visual caps instead of repeating a single sprite.
module Platformer.Stamps

open Mibo.Layout
open Platformer.Types

// -----------------------------------------------------------
// Internal helpers
// -----------------------------------------------------------

/// Horizontal row with distinct start/middle/end tiles.
///   length ≤ 0 → no-op
///   length 1   → singleTile only (standalone sprite)
///   length 2   → start + end
///   length ≥ 3 → start + middle × (length-2) + end
let hRow
  (length: int)
  (startTile: Tile)
  (middleTile: Tile)
  (endTile: Tile)
  (singleTile: Tile)
  (section: GridSection2D<Tile>)
  : GridSection2D<Tile> =
  match length with
  | n when n <= 0 -> section
  | 1 -> section |> Layout.set 0 0 singleTile
  | 2 -> section |> Layout.set 0 0 startTile |> Layout.set 1 0 endTile
  | _ ->
    section
    |> Layout.set 0 0 startTile
    |> Layout.repeatX 1 0 (length - 2) middleTile
    |> Layout.set (length - 1) 0 endTile

// -----------------------------------------------------------
// Biome-parameterized stamps
// -----------------------------------------------------------

/// Visible top surface of solid ground (e.g. grass-topped dirt).
/// BlockTopLeft / BlockTop / BlockTopRight, or Block when length is 1.
let inline topPlatform
  (biome: Biome)
  (length: int)
  (section: GridSection2D<Tile>)
  =
  hRow
    length
    (BlockTopLeft biome)
    (BlockTop biome)
    (BlockTopRight biome)
    (Block biome)
    section

/// Visible bottom surface (e.g. ceiling under overhang).
/// BlockBottomLeft / BlockBottom / BlockBottomRight, or Block when length is 1.
let inline bottomPlatform
  (biome: Biome)
  (length: int)
  (section: GridSection2D<Tile>)
  =
  hRow
    length
    (BlockBottomLeft biome)
    (BlockBottom biome)
    (BlockBottomRight biome)
    (Block biome)
    section

/// Fully solid middle row (no visible top or bottom edge).
/// BlockLeft / BlockCenter / BlockRight, or Block when length is 1.
let inline solidRow
  (biome: Biome)
  (length: int)
  (section: GridSection2D<Tile>)
  =
  hRow
    length
    (BlockLeft biome)
    (BlockCenter biome)
    (BlockRight biome)
    (Block biome)
    section

/// One-way cloud platform (pass-through from below, land from above).
/// CloudLeft / CloudMiddle / CloudRight, or Cloud when length is 1.
let inline floatingPlatform
  (biome: Biome)
  (length: int)
  (section: GridSection2D<Tile>)
  =
  hRow
    length
    (CloudLeft biome)
    (CloudMiddle biome)
    (CloudRight biome)
    (Cloud biome)
    section

/// Thin horizontal ledge (partial-height collider).
/// HorizontalLeft / Horizontal / HorizontalRight.
/// No distinct standalone sprite, so Horizontal is used for length 1.
let inline ledge (biome: Biome) (length: int) (section: GridSection2D<Tile>) =
  hRow
    length
    (HorizontalLeft biome)
    (Horizontal biome)
    (HorizontalRight biome)
    (Horizontal biome)
    section

/// Vertical wall built on Platformer.pillar (top/middle/bottom).
/// VerticalTop at the top, VerticalMiddle in between, VerticalBottom at the base.
let inline wall (biome: Biome) (height: int) (section: GridSection2D<Tile>) =
  section
  |> Platformer.pillar
    height
    (VerticalBottom biome)
    (VerticalMiddle biome)
    (VerticalTop biome)

/// Closed ground section: top edge + fill rows + bottom edge.
/// Fully sealed — no entrances or exits. Interior fill uses BlockCenter
/// (passable — no collider). Use `island` when you need internal content.
///
///   height ≤ 0 → no-op
///   height  1  → topPlatform only
///   height  2  → topPlatform + bottomPlatform
///   height ≥ 3 → topPlatform + solidRow × (height-2) + bottomPlatform
let ground
  (biome: Biome)
  (width: int)
  (height: int)
  (section: GridSection2D<Tile>)
  =
  if height <= 0 || width <= 0 then
    section
  elif height = 1 then
    topPlatform biome width section
  else
    Layout.section 0 0 (topPlatform biome width) section |> ignore

    if height > 2 then
      for row in 1 .. height - 2 do
        Layout.section 0 row (solidRow biome width) section |> ignore

    Layout.section 0 (height - 1) (bottomPlatform biome width) section |> ignore
    section

// -----------------------------------------------------------
// Cloud ledge (CloudBackground — embedded jumpable edge in a wall)
// -----------------------------------------------------------

/// A one-way ledge embedded in solid terrain (CloudBackground).
/// Represents a jumpable edge inside a cave wall — not repeated.
/// Place within a solid section to create a surface the player can land on.
let inline cloudLedge (biome: Biome) (section: GridSection2D<Tile>) =
  Layout.set 0 0 (CloudBackground biome) section

// -----------------------------------------------------------
// Ramps (fixed-width multi-segment slopes)
// The slope surface is baked into the sprite art + collision polygon.
// -----------------------------------------------------------

/// Long ramp (fixed 3-wide). RampLongA → RampLongB → RampLongC left-to-right.
let inline longRamp (biome: Biome) (section: GridSection2D<Tile>) =
  section
  |> Layout.set 0 0 (RampLongA biome)
  |> Layout.set 1 0 (RampLongB biome)
  |> Layout.set 2 0 (RampLongC biome)

/// Short ramp (fixed 2-wide). RampShortA → RampShortB left-to-right.
let inline shortRamp (biome: Biome) (section: GridSection2D<Tile>) =
  section
  |> Layout.set 0 0 (RampShortA biome)
  |> Layout.set 1 0 (RampShortB biome)

// -----------------------------------------------------------
// Biome convenience modules — partial application of biome
// -----------------------------------------------------------

module Grass =
  let inline topPlatform length = topPlatform Grass length
  let inline bottomPlatform length = bottomPlatform Grass length
  let inline solidRow length = solidRow Grass length
  let inline floatingPlatform length = floatingPlatform Grass length
  let inline ledge length = ledge Grass length
  let inline wall height = wall Grass height
  let inline cloudLedge section = cloudLedge Grass section
  let inline longRamp section = longRamp Grass section
  let inline shortRamp section = shortRamp Grass section

module Dirt =
  let inline topPlatform length = topPlatform Dirt length
  let inline bottomPlatform length = bottomPlatform Dirt length
  let inline solidRow length = solidRow Dirt length
  let inline floatingPlatform length = floatingPlatform Dirt length
  let inline ledge length = ledge Dirt length
  let inline wall height = wall Dirt height
  let inline cloudLedge section = cloudLedge Dirt section
  let inline longRamp section = longRamp Dirt section
  let inline shortRamp section = shortRamp Dirt section

module Stone =
  let inline topPlatform length = topPlatform Stone length
  let inline bottomPlatform length = bottomPlatform Stone length
  let inline solidRow length = solidRow Stone length
  let inline floatingPlatform length = floatingPlatform Stone length
  let inline ledge length = ledge Stone length
  let inline wall height = wall Stone height
  let inline cloudLedge section = cloudLedge Stone section
  let inline longRamp section = longRamp Stone section
  let inline shortRamp section = shortRamp Stone section

module Snow =
  let inline topPlatform length = topPlatform Snow length
  let inline bottomPlatform length = bottomPlatform Snow length
  let inline solidRow length = solidRow Snow length
  let inline floatingPlatform length = floatingPlatform Snow length
  let inline ledge length = ledge Snow length
  let inline wall height = wall Snow height
  let inline cloudLedge section = cloudLedge Snow section
  let inline longRamp section = longRamp Snow section
  let inline shortRamp section = shortRamp Snow section

module Sand =
  let inline topPlatform length = topPlatform Sand length
  let inline bottomPlatform length = bottomPlatform Sand length
  let inline solidRow length = solidRow Sand length
  let inline floatingPlatform length = floatingPlatform Sand length
  let inline ledge length = ledge Sand length
  let inline wall height = wall Sand height
  let inline cloudLedge section = cloudLedge Sand section
  let inline longRamp section = longRamp Sand section
  let inline shortRamp section = shortRamp Sand section

module Purple =
  let inline topPlatform length = topPlatform Purple length
  let inline bottomPlatform length = bottomPlatform Purple length
  let inline solidRow length = solidRow Purple length
  let inline floatingPlatform length = floatingPlatform Purple length
  let inline ledge length = ledge Purple length
  let inline wall height = wall Purple height
  let inline cloudLedge section = cloudLedge Purple section
  let inline longRamp section = longRamp Purple section
  let inline shortRamp section = shortRamp Purple section
