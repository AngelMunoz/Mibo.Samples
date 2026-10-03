module LiveMap.Semantics.Flat

open System.Collections.Frozen
open System.Collections.Generic
open LiveMap
open LiveMap.Semantics
open Mibo.Layout
open Mibo.Markup

/// The document surface for flat maps: the three tables a `.kdl` or
/// `.xml` file resolves against.
///
/// ```
/// words     fill grass  ·  set 3 4 treePine  ·  rect stone stoneB
/// kernels   generate forest  ·  generate 0 4 40 1 road
/// elements  grove  ·  meadow  ·  camp  ·  yard  ·  court  ·  cache
/// ```
///
/// A document extends the element library with its own
/// `element name { ... }` declarations. It cannot add words or kernels:
/// those are the game's vocabulary, and `Doc.resolve` fails the build
/// with the document's line and column when a name is not here.

let private frozen(pairs: (string * 'T) list) : FrozenDictionary<string, 'T> =
  let dictionary = Dictionary<string, 'T>(pairs.Length)

  for key, value in pairs do
    dictionary[key] <- value

  dictionary.ToFrozenDictionary()

/// One entry of the element library. An element is a body of paint
/// statements, not a Flow stamp: the statement set is closed, so a game
/// extends a document through words, kernels, and element bodies.
let private element
  (name: string)
  (extent: CellSize voption)
  (body: Doc.Op<Cell>[])
  : string * Doc.ElementDecl<Cell> =
  name,
  {
    Name = name
    Extent = extent
    Body = body
  }

/// A one-cell element for a word. A prop a document can name as a node —
/// `crate`, `treePine` — reads as a list inside a scatter plot, where
/// `set x y word` statements would read as coordinates.
let private prop (name: string) (cell: Cell) : string * Doc.ElementDecl<Cell> =
  element name (ValueSome { W = 1; H = 1 }) [| Doc.Op.Fill cell |]

let surface: Doc.Surface<Cell> = {
  Words = frozen(List.ofArray Palette.words)
  Kernels =
    frozen [
      // pure functions: they tile over any area without a seam
      "meadow", Terrain.meadow
      "forest", Terrain.forest
      "gravel", Terrain.gravel
      "road", Terrain.road
      "track", Terrain.track
      // stamps: a fixed silhouette, repeated when the area is larger
      "camp", Bake.kernel 5 5 Palette.grass Settlement.camp
      "outpost", Bake.kernel 7 7 Palette.grass Settlement.outpost
      "plaza", Bake.kernel 7 5 Palette.stoneB Settlement.plaza
    ]
  Elements =
    frozen [
      // areas
      element "grove" ValueNone [| Doc.Op.Generate("forest", ValueNone) |]
      element "meadow" ValueNone [| Doc.Op.Generate("meadow", ValueNone) |]
      element "camp" (ValueSome { W = 5; H = 5 }) [|
        Doc.Op.Generate("camp", ValueNone)
      |]
      element "yard" ValueNone [|
        Doc.Op.Fill Palette.dirt
        Doc.Op.Border(ValueNone, Palette.crateBeveled)
      |]
      element "court" ValueNone [| Doc.Op.Rect(Palette.stone, Palette.stoneB) |]
      element "cache" ValueNone [|
        Doc.Op.Fill Palette.dirtB
        Doc.Op.Set(ValueNone, Center, Center, Palette.coin)
      |]
      // props
      prop "bush" Palette.bush
      prop "rockSmall" Palette.rockSmall
      prop "rockMedium" Palette.rockMedium
      prop "rockLarge" Palette.rockLarge
      prop "treeRound" Palette.treeRound
      prop "treePine" Palette.treePine
      prop "crate" Palette.crate
      prop "crateBeveled" Palette.crateBeveled
      prop "container" Palette.container
      prop "containerLarge" Palette.containerLarge
      prop "turretMount" Palette.turretMount
      prop "turretBase" Palette.turretBase
      prop "coin" Palette.coin
    ]
  // a flat sprite covers its cell, and no statement sizes one
  Span = ValueNone
  WithSpan = ValueNone
}
