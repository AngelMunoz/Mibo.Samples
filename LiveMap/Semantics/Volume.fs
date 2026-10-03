module LiveMap.Semantics.Volume

open System.Collections.Frozen
open System.Collections.Generic
open System
open LiveMap
open LiveMap.Semantics
open Mibo.Layout
open Mibo.Markup

/// The document surface for block maps. Same three tables as the flat
/// mode, different vocabulary:
///
/// ```
/// words     fill grass  ·  set 2 3 wall  ·  fill snow
/// kernels   generate hill  ·  generate wood
/// elements  field  ·  wood  ·  hill  ·  rampart  ·  pit  ·  hut
/// ```
///
/// The vertical axis is not part of the document: a cell carries its own
/// height, so a map stays a two-dimensional footprint and the third
/// dimension comes from what a word means.

let private frozen(pairs: (string * 'T) list) : FrozenDictionary<string, 'T> =
  let dictionary = Dictionary<string, 'T>(pairs.Length)

  for key, value in pairs do
    dictionary[key] <- value

  dictionary.ToFrozenDictionary()

/// Rolling ground: grass at three heights.
///
/// A kernel paints every cell of its area, so the rules here are the
/// ground itself; the plants and the loose stones that stand on it are
/// elements the document scatters in its decor layer.
let field: Doc.Kernel<BlockCell> =
  Doc.Gen2(fun x y ->
    if Noise.chance x y 13 18u then Blocks.grassLow
    elif Noise.chance x y 14 10u then Blocks.grassEdge
    else Blocks.grass)

/// A hill: columns rise and fall on a sine of the row, so the slope is
/// continuous instead of stepped at random.
let hill: Doc.Kernel<BlockCell> =
  Doc.Gen2(fun x y ->
    let rise = 1.0f + 2.0f * float32(sin(float y * 0.45))
    let jitter = Noise.fraction x y 21 * 0.5f

    if rise + jitter < 1.4f then
      Blocks.grassLow
    else
      BlockCell.column Blocks.grass.Model (rise + jitter) true)

/// A stand of trees over rolled ground.
let wood: Doc.Kernel<BlockCell> =
  Doc.Gen2(fun x y ->
    let canopy = Noise.chance (x / 3) (y / 3) 31 58u

    if canopy && Noise.chance x y 32 42u then Blocks.pine
    elif canopy && Noise.chance x y 33 16u then Blocks.tree
    elif Noise.chance x y 34 8u then Blocks.stones
    else Blocks.grassLow)

/// Snow, with drifts. The snow trees and the bare rock go with it, and
/// the document scatters them in its decor layer.
let snowfield: Doc.Kernel<BlockCell> =
  Doc.Gen2(fun x y ->
    if Noise.chance x y 43 22u then
      Blocks.snowLow
    else
      Blocks.snow)

let private element
  (name: string)
  (extent: CellSize voption)
  (body: Doc.Op<BlockCell>[])
  : string * Doc.ElementDecl<BlockCell> =
  name,
  {
    Name = name
    Extent = extent
    Body = body
  }

/// A one-cell element for a word, so a document can name a planted or
/// built thing as a node.
let private prop
  (name: string)
  (cell: BlockCell)
  : string * Doc.ElementDecl<BlockCell> =
  element name (ValueSome { W = 1; H = 1 }) [| Doc.Op.Fill cell |]

/// A ground piece as a node, so a document places ground through a flow
/// container by name alone. The element states no size: the piece's own span
/// measures it, so a two-cell piece reserves two cells of the row it flows
/// into.
let private ground
  (name: string)
  (cell: BlockCell)
  : string * Doc.ElementDecl<BlockCell> =
  element name ValueNone [|
    Doc.Op.Set(ValueSome { X = 0; Y = 0 }, Start, Start, cell)
  |]

let surface: Doc.Surface<BlockCell> = {
  Words = frozen(List.ofArray Blocks.words)
  Kernels =
    frozen [
      "field", field
      "hill", hill
      "wood", wood
      "snowfield", snowfield
      "hut", Bake.kernel 3 3 Blocks.grass Build.hut
      "tower", Bake.kernel 3 3 Blocks.grass Build.tower
      "yard", Bake.kernel 5 5 Blocks.stones Build.yard
      "outpost", Bake.kernel 4 4 Blocks.snowLow Build.outpost
    ]
  Elements =
    frozen [
      element "field" ValueNone [| Doc.Op.Generate("field", ValueNone) |]
      element "wood" ValueNone [| Doc.Op.Generate("wood", ValueNone) |]
      element "hill" ValueNone [| Doc.Op.Generate("hill", ValueNone) |]
      element "rampart" ValueNone [|
        Doc.Op.Fill Blocks.grass
        Doc.Op.Border(ValueNone, Blocks.wall)
      |]
      element "pit" ValueNone [|
        Doc.Op.Fill Blocks.stones
        Doc.Op.Set(ValueNone, Center, Center, Blocks.coin)
      |]
      element "hut" (ValueSome { W = 3; H = 3 }) [|
        Doc.Op.Generate("hut", ValueNone)
      |]
      // props
      prop "tree" Blocks.tree
      prop "pine" Blocks.pine
      prop "pineSmall" Blocks.pineSmall
      prop "snowTree" Blocks.snowTree
      prop "hedge" Blocks.hedge
      prop "stones" Blocks.stones
      prop "crate" Blocks.crate
      prop "crateStrong" Blocks.crateStrong
      prop "barrel" Blocks.barrel
      prop "fence" Blocks.fence
      prop "ladder" Blocks.ladder
      prop "flag" Blocks.flag
      prop "coin" Blocks.coin
      // ground pieces, so a flow container lays the ground out by name
      ground "grass" Blocks.grass
      ground "grassLow" Blocks.grassLow
      ground "grassLarge" Blocks.grassLarge
      ground "grassLowLarge" Blocks.grassLowLarge
      ground "grassTall" Blocks.grassTall
      ground "grassLong" Blocks.grassLong
      ground "grassLowLong" Blocks.grassLowLong
      ground "snow" Blocks.snow
      ground "snowLow" Blocks.snowLow
      ground "snowLarge" Blocks.snowLarge
      ground "snowLowLarge" Blocks.snowLowLarge
      ground "snowLong" Blocks.snowLong
      ground "snowEdge" Blocks.snowEdge
      ground "snowCorner" Blocks.snowCorner
      ground "grassEdge" Blocks.grassEdge
      ground "grassCorner" Blocks.grassCorner
    ]
  // a plate reads the span it carries, and a `set` may size one
  Span = ValueSome(fun cell -> cell.Span)
  WithSpan = ValueSome(fun cell span -> { cell with Span = span })
}
