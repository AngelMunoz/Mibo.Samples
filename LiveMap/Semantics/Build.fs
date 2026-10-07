module LiveMap.Semantics.Build

open LiveMap
open LiveMap.Semantics.Blocks
open Mibo.Layout

/// Built things as Flow stamps, for the block map. Each one states its
/// own footprint so a container can place it, and a document can name it
/// through a baked kernel.
/// A hut: a platform floor inside a fence ring, three cells square.
let hut: Stamp<BlockCell> =
  Stamp.box 3 3 [ Flow.fill platform; Flow.border fence ]

/// A tower: a grass base walled with pillars, three cells square.
let tower: Stamp<BlockCell> =
  Stamp.box 3 3 [ Flow.fill grassLow; Flow.border pillar ]

/// A yard of crates behind a fence, five cells square.
let yard: Stamp<BlockCell> =
  Stamp.box 5 5 [
    Flow.fill stones
    Flow.border fence
    Flow.cell { X = 2; Y = 2 } crate
    Flow.cell { X = 1; Y = 1 } barrel
    Flow.cell { X = 3; Y = 3 } crateStrong
  ]

/// A snowfield outpost: a platform pad with a flag, four cells square.
let outpost: Stamp<BlockCell> =
  Stamp.box 4 4 [
    Flow.fill snowLow
    Flow.border wall
    Flow.cell { X = 1; Y = 1 } flag
  ]
