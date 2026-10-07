module LiveMap.Semantics.Palette

open System.Collections.Frozen
open System.Collections.Generic
open LiveMap
open LiveMap.Catalog

/// A cell that paints one atlas tile and states whether that tile blocks
/// movement.
let private tile (info: TileInfo) (solid: bool) : Cell = {
  Tile = info
  Solid = solid
}

// ── ground ───────────────────────────────────────────────────
// Walkable material. The lettered variants are the same material with
// different paint, which is how a wide field avoids looking tiled.

let grass = tile Tiles.grass false
let grassB = tile Tiles.grassB false
let grassC = tile Tiles.grassC false
let dirt = tile Tiles.dirt false
let dirtB = tile Tiles.dirtB false
let dirtC = tile Tiles.dirtC false
let sand = tile Tiles.sand false
let sandB = tile Tiles.sandB false
let sandC = tile Tiles.sandC false
let stone = tile Tiles.stone false
let stoneB = tile Tiles.stoneB false
let stoneC = tile Tiles.stoneC false

// ── roads ────────────────────────────────────────────────────
// Straight and end pieces, in two materials, for laying routes.

let pathVerticalDirt = tile Tiles.pathVerticalDirt false
let pathHorizontalDirt = tile Tiles.pathHorizontalDirt false
let pathEndUpDirt = tile Tiles.pathEndUpDirt false
let pathEndLeftDirt = tile Tiles.pathEndLeftDirt false
let pathVerticalStone = tile Tiles.pathVerticalStone false
let pathHorizontalStone = tile Tiles.pathHorizontalStone false
let pathEndUpStone = tile Tiles.pathEndUpStone false
let pathEndLeftStone = tile Tiles.pathEndLeftStone false

// ── props ────────────────────────────────────────────────────
// Planted things. Trees, rocks, and containers stop movement; a bush, a
// coin, and bare ground do not.

let bush = tile Tiles.bush false
let rockSmall = tile Tiles.rockSmall true
let rockMedium = tile Tiles.rockMedium true
let rockLarge = tile Tiles.rockLarge true
let treeRound = tile Tiles.treeRound true
let treePine = tile Tiles.treePine true
let crate = tile Tiles.crate true
let crateBeveled = tile Tiles.crateBeveled true
let container = tile Tiles.container true
let containerLarge = tile Tiles.containerLarge true
let coin = tile Tiles.coin false
let turretMount = tile Tiles.turretMount true
let turretBase = tile Tiles.turretBase true

// ── blends ───────────────────────────────────────────────────
// One material painted onto another: a speck or a patch. These are the
// vocabulary for the edge between two regions, where a hard line would
// read as a seam.

let grassDotOnDirt = tile Tiles.grassDotOnDirt false
let sandDotOnDirt = tile Tiles.sandDotOnDirt false
let stoneDotOnDirt = tile Tiles.stoneDotOnDirt false
let dirtDotOnGrass = tile Tiles.dirtDotOnGrass false
let sandDotOnGrass = tile Tiles.sandDotOnGrass false
let stoneDotOnGrass = tile Tiles.stoneDotOnGrass false
let grassDotOnSand = tile Tiles.grassDotOnSand false
let dirtDotOnSand = tile Tiles.dirtDotOnSand false
let stoneDotOnSand = tile Tiles.stoneDotOnSand false
let grassDotOnStone = tile Tiles.grassDotOnStone false
let dirtDotOnStone = tile Tiles.dirtDotOnStone false
let sandDotOnStone = tile Tiles.sandDotOnStone false

let grassPatchOnDirt = tile Tiles.grassPatchOnDirt false
let sandPatchOnDirt = tile Tiles.sandPatchOnDirt false
let stonePatchOnDirt = tile Tiles.stonePatchOnDirt false
let dirtPatchOnGrass = tile Tiles.dirtPatchOnGrass false
let sandPatchOnGrass = tile Tiles.sandPatchOnGrass false
let stonePatchOnGrass = tile Tiles.stonePatchOnGrass false
let grassPatchOnSand = tile Tiles.grassPatchOnSand false
let dirtPatchOnSand = tile Tiles.dirtPatchOnSand false
let stonePatchOnSand = tile Tiles.stonePatchOnSand false
let grassPatchOnStone = tile Tiles.grassPatchOnStone false
let dirtPatchOnStone = tile Tiles.dirtPatchOnStone false
let sandPatchOnStone = tile Tiles.sandPatchOnStone false

/// The words a flat map document may use: `fill grass`, `set 3 4 pine`.
///
/// This table is the mode's whole vocabulary. The F# stamps paint the
/// same values, and `Doc.resolve` resolves a document against the
/// table, so a word that is not here fails the build and names the
/// document's own line and column.
let words: (string * Cell)[] = [|
  "grass", grass
  "grassB", grassB
  "grassC", grassC
  "dirt", dirt
  "dirtB", dirtB
  "dirtC", dirtC
  "sand", sand
  "sandB", sandB
  "sandC", sandC
  "stone", stone
  "stoneB", stoneB
  "stoneC", stoneC
  "pathVerticalDirt", pathVerticalDirt
  "pathHorizontalDirt", pathHorizontalDirt
  "pathEndUpDirt", pathEndUpDirt
  "pathEndLeftDirt", pathEndLeftDirt
  "pathVerticalStone", pathVerticalStone
  "pathHorizontalStone", pathHorizontalStone
  "pathEndUpStone", pathEndUpStone
  "pathEndLeftStone", pathEndLeftStone
  "bush", bush
  "rockSmall", rockSmall
  "rockMedium", rockMedium
  "rockLarge", rockLarge
  "treeRound", treeRound
  "treePine", treePine
  "crate", crate
  "crateBeveled", crateBeveled
  "container", container
  "containerLarge", containerLarge
  "coin", coin
  "turretMount", turretMount
  "turretBase", turretBase
  "grassDotOnDirt", grassDotOnDirt
  "sandDotOnDirt", sandDotOnDirt
  "stoneDotOnDirt", stoneDotOnDirt
  "dirtDotOnGrass", dirtDotOnGrass
  "sandDotOnGrass", sandDotOnGrass
  "stoneDotOnGrass", stoneDotOnGrass
  "grassDotOnSand", grassDotOnSand
  "dirtDotOnSand", dirtDotOnSand
  "stoneDotOnSand", stoneDotOnSand
  "grassDotOnStone", grassDotOnStone
  "dirtDotOnStone", dirtDotOnStone
  "sandDotOnStone", sandDotOnStone
  "grassPatchOnDirt", grassPatchOnDirt
  "sandPatchOnDirt", sandPatchOnDirt
  "stonePatchOnDirt", stonePatchOnDirt
  "dirtPatchOnGrass", dirtPatchOnGrass
  "sandPatchOnGrass", sandPatchOnGrass
  "stonePatchOnGrass", stonePatchOnGrass
  "grassPatchOnSand", grassPatchOnSand
  "dirtPatchOnSand", dirtPatchOnSand
  "stonePatchOnSand", stonePatchOnSand
  "grassPatchOnStone", grassPatchOnStone
  "dirtPatchOnStone", dirtPatchOnStone
  "sandPatchOnStone", sandPatchOnStone
|]

let private wordIndex: FrozenDictionary<string, string> =
  let index = Dictionary<string, string>()

  for word, cell in words do
    if not(index.ContainsKey cell.Tile.Name) then
      index[cell.Tile.Name] <- word

  index.ToFrozenDictionary()

/// The word that paints an atlas tile, for the hover panel. A kernel can
/// build a cell that no word names; that cell reports an empty word.
let wordOfTile(tile: TileInfo) : string =
  match wordIndex.TryGetValue tile.Name with
  | true, word -> word
  | false, _ -> ""
