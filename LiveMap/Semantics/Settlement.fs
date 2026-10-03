module LiveMap.Semantics.Settlement

open LiveMap
open LiveMap.Semantics.Palette
open Mibo.Layout

/// Built things, as Flow stamps: a fixed silhouette the game composed
/// once, which a document can name through a baked kernel or that F#
/// code paints directly.
///
/// Each stamp declares its own footprint, so a container can place it
/// without the author counting cells. The size in each doc comment is the
/// size a document must state when it generates the stamp from a kernel,
/// unless it wants the stamp tiled.
/// A walled yard: packed dirt inside a crate wall, with a turret mount
/// at its center. Five cells square.
let camp: Stamp<Cell> =
  Stamp.box 5 5 [
    Flow.fill dirtB
    Flow.border crate
    Flow.cell { X = 2; Y = 2 } turretMount
  ]

/// A stone plaza with a coin in the middle and benches around it.
/// Seven by five.
let plaza: Stamp<Cell> =
  Stamp.box 7 5 [
    Flow.rect stone stoneB
    Flow.cell { X = 3; Y = 2 } coin
    Flow.cell { X = 1; Y = 1 } bush
    Flow.cell { X = 5; Y = 3 } bush
  ]

/// A trading post: a crate compound on a packed-dirt pad, with stores
/// against its north wall and a coin in the yard. Seven cells square.
let outpost: Stamp<Cell> =
  Stamp.box 7 7 [
    Flow.fill dirt
    Flow.border crateBeveled
    Flow.cell { X = 3; Y = 3 } coin
    Flow.cell { X = 2; Y = 2 } container
    Flow.cell { X = 4; Y = 2 } containerLarge
  ]
